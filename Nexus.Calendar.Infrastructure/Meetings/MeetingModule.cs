using System.Globalization;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Calendar.Application.Meetings;
using Nexus.Calendar.Permissions;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Messaging;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Infrastructure.Meetings;

public sealed class MeetingRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid OrganizerUserId { get; set; }
    public DateOnly Date { get; set; }
    public string Content { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<MeetingParticipant> Participants { get; set; } = [];
}
public sealed class MeetingParticipant
{
    public Guid MeetingId { get; set; }
    public Guid UserId { get; set; }
}
public sealed class MeetingDbContext(DbContextOptions<MeetingDbContext> options) : DbContext(options)
{
    public DbSet<MeetingRecord> Meetings => Set<MeetingRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var meeting = model.Entity<MeetingRecord>();
        meeting.ToTable("Meetings", "calendar"); meeting.HasKey(m => m.Id);
        meeting.Property(m => m.Id).ValueGeneratedNever();
        meeting.Property(m => m.Date).HasColumnType("date");
        meeting.Property(m => m.Content).IsRequired();
        meeting.Property(m => m.Version).IsConcurrencyToken();
        meeting.HasIndex(m => new { m.TenantId, m.Date });
        meeting.HasMany(m => m.Participants).WithOne().HasForeignKey(p => p.MeetingId).OnDelete(DeleteBehavior.Cascade);
        var participant = model.Entity<MeetingParticipant>();
        participant.ToTable("MeetingParticipants", "calendar"); participant.HasKey(p => new { p.MeetingId, p.UserId });
        participant.HasIndex(p => p.UserId);
    }
}

public sealed class MeetingService(MeetingDbContext db, NexusCoreDbContext identity, ICurrentUserContext current,
    ISmsTemplateService sms, ILogger<MeetingService> logger, IMeetingPeopleScope peopleScope,
    IMeetingTaskDirectory taskDirectory, ISchedulingDayPolicy? schedulingDays = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Task administration is not permission to inspect unrelated meetings.
    public async Task<Result<MeetingPeopleOptions>> PeopleAsync(CancellationToken ct)
    {
        if (current.UserId is null || current.TenantId is null) return Result.Failure<MeetingPeopleOptions>(Error.Unauthorized());
        if (!current.HasPermission(MeetingPermissions.View) && !current.HasPermission(MeetingPermissions.Create) && !current.HasPermission(MeetingPermissions.Edit))
            return Result.Failure<MeetingPeopleOptions>(Error.Forbidden("دسترسی به بخش جلسات را ندارید."));
        return Result.Success(await peopleScope.GetAsync(ct));
    }
    private bool AccessAll => current.HasPermission(MeetingPermissions.AccessAllPeople);
    private bool Editable(MeetingRecord m) => AccessAll || m.CreatedByUserId == current.UserId || m.OrganizerUserId == current.UserId;
    private async Task<HashSet<Guid>> VisiblePeopleAsync(CancellationToken ct) =>
        (await peopleScope.GetAsync(ct)).People.Select(person => person.Id).Append(current.UserId!.Value).ToHashSet();
    private static bool Touches(MeetingRecord meeting, IReadOnlySet<Guid> people) =>
        people.Contains(meeting.CreatedByUserId) || people.Contains(meeting.OrganizerUserId) || meeting.Participants.Any(p => people.Contains(p.UserId));
    private MeetingDto Dto(MeetingRecord m, string? notice = null) => new(m.Id, m.CreatedByUserId, m.Version,
        JsonSerializer.Deserialize<MeetingInput>(m.Content, Json)!, Editable(m) && current.HasPermission(MeetingPermissions.Edit),
        Editable(m) && current.HasPermission(MeetingPermissions.Edit), notice);

    public async Task<Result<MeetingTaskPage>> SearchTasksAsync(string? search, int pageNumber, int pageSize, string? ids, CancellationToken ct)
    {
        if (current.UserId is null || current.TenantId is null) return Result.Failure<MeetingTaskPage>(Error.Unauthorized());
        return await taskDirectory.SearchAsync(search, pageNumber, pageSize, ids, ct);
    }

    public async Task<Result<IReadOnlyList<MeetingDto>>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (current.TenantId is null || current.UserId is null) return Result.Failure<IReadOnlyList<MeetingDto>>(Error.Unauthorized());
        if (!current.HasPermission(MeetingPermissions.View)) return Result.Failure<IReadOnlyList<MeetingDto>>(Error.Forbidden());
        if (to <= from || to.DayNumber - from.DayNumber > 370) return Result.Failure<IReadOnlyList<MeetingDto>>(Error.Validation("بازه تقویم معتبر نیست."));
        var visiblePeople = await VisiblePeopleAsync(ct);
        var rows = await db.Meetings.Include(m => m.Participants).AsNoTracking()
            .Where(m => m.TenantId == current.TenantId && m.Date >= from && m.Date < to).OrderBy(m => m.Date).ToListAsync(ct);
        rows = rows.Where(meeting => AccessAll || Touches(meeting, visiblePeople)).ToList();
        return Result.Success<IReadOnlyList<MeetingDto>>(rows.Select(m => Dto(m)).OrderBy(m => m.Details.Date).ThenBy(m => m.Details.StartTime).ToList());
    }

    private static HashSet<Guid> MeetingPeople(MeetingInput meeting) => meeting.ParticipantIds.Append(meeting.OrganizerUserId)
        .Concat(meeting.Agenda.Select(item => item.PresenterUserId).OfType<Guid>())
        .Concat(meeting.Actions.Select(item => item.AssigneeUserId).OfType<Guid>()).ToHashSet();
    private async Task<string?> SchedulingConflictAsync(Guid? id, MeetingInput input, CancellationToken ct)
    {
        var start = TimeOnly.ParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        var end = TimeOnly.ParseExact(input.EndTime, "HH:mm", CultureInfo.InvariantCulture);
        var people = MeetingPeople(input);
        var location = input.Location?.Trim();
        var rows = await db.Meetings.Include(m => m.Participants).AsNoTracking()
            .Where(m => m.TenantId == current.TenantId && m.Date == input.Date && (id == null || m.Id != id)).ToListAsync(ct);
        foreach (var row in rows)
        {
            var other = JsonSerializer.Deserialize<MeetingInput>(row.Content, Json)!;
            if (!TimeOnly.TryParseExact(other.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var otherStart)
                || !TimeOnly.TryParseExact(other.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var otherEnd)
                || start >= otherEnd || otherStart >= end) continue;
            var shared = people.Intersect(MeetingPeople(other)).ToList();
            if (shared.Count > 0)
            {
                var names = await identity.Users.AsNoTracking().Where(user => shared.Contains(user.Id)).Select(user => user.DisplayName).ToListAsync(ct);
                return $"تداخل زمانی جلسه برای {string.Join("، ", names)} وجود دارد؛ یک فرد نمی‌تواند در یک بازه در دو جلسه باشد.";
            }
            if (!string.IsNullOrWhiteSpace(location) && string.Equals(location, other.Location?.Trim(), StringComparison.OrdinalIgnoreCase))
                return "این مکان در تاریخ و بازه زمانی انتخاب‌شده برای جلسه دیگری رزرو شده است.";
        }
        return null;
    }

    private async Task<string?> ValidateAsync(MeetingInput x, bool checkDay, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 200) return "عنوان جلسه را حداکثر در ۲۰۰ کاراکتر وارد کنید.";
        if (x.Type is not ("meeting" or "online" or "committee")) return "نوع جلسه معتبر نیست.";
        if (x.Date.Year is < 1900 or > 2100) return "تاریخ جلسه معتبر نیست.";
        var schedulingDecision = checkDay ? (schedulingDays ?? new AllowAllSchedulingDays()).Check(x.Date) : SchedulingDayDecision.Allowed;
        if (!schedulingDecision.IsAllowed) return schedulingDecision.Reason;
        if (!TimeOnly.TryParseExact(x.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(x.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
            return "ساعت پایان باید بعد از ساعت شروع همان روز باشد.";
        if (x.Location?.Length > 300 || x.Description?.Length > 4000 || x.Minutes?.Length > 12000) return "متن مشخصات یا صورت‌جلسه بیش از حد مجاز است.";
        if (x.ParticipantIds is null || x.Agenda is null || x.Actions is null || x.TaskIds is null
            || x.ParticipantIds.Count > 100 || x.Agenda.Count > 50 || x.Actions.Count > 50 || x.TaskIds.Count > 100)
            return "تعداد ردیف‌های جلسه بیش از حد مجاز است.";
        if (x.Agenda.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 500 || a.DurationMinutes is < 1 or > 480)) return "عنوان و مدت هر بند دستور جلسه را تکمیل کنید.";
        if (x.Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 1000 || a.Status is not ("todo" or "in_progress" or "completed"))) return "عنوان و وضعیت اقدامات را تکمیل کنید.";
        var people = x.ParticipantIds.Append(x.OrganizerUserId).Concat(x.Agenda.Select(a => a.PresenterUserId).OfType<Guid>())
            .Concat(x.Actions.Select(a => a.AssigneeUserId).OfType<Guid>()).Distinct().ToList();
        var valid = await identity.Users.Where(u => u.TenantId == current.TenantId && u.IsActive && people.Contains(u.Id)).CountAsync(ct);
        if (valid != people.Count) return "افراد جلسه باید از کاربران فعال همین سازمان باشند.";
        foreach (var taskId in x.TaskIds.Distinct())
            if (!current.HasPermission("Tasks.View") || !await taskDirectory.CanAccessAsync(taskId, ct)) return "یکی از وظایف انتخاب‌شده قابل دسترسی نیست.";
        return null;
    }

    public async Task<Result<MeetingDto>> SaveAsync(Guid? id, MeetingInput input, CancellationToken ct)
    {
        if (current.TenantId is null || current.UserId is null) return Result.Failure<MeetingDto>(Error.Unauthorized());
        if (!current.HasPermission(id is null ? MeetingPermissions.Create : MeetingPermissions.Edit))
            return Result.Failure<MeetingDto>(Error.Forbidden("دسترسی ثبت یا ویرایش جلسه را ندارید."));
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        MeetingRecord? row = null; MeetingInput? before = null;
        if (id is { } existing)
        {
            row = await db.Meetings.Include(m => m.Participants).FirstOrDefaultAsync(m => m.TenantId == current.TenantId && m.Id == existing, ct);
            if (row is null) return Result.Failure<MeetingDto>(Error.NotFound("جلسه پیدا نشد."));
            var visible = await VisiblePeopleAsync(ct);
            if (!AccessAll && !Touches(row, visible)) return Result.Failure<MeetingDto>(Error.NotFound("جلسه پیدا نشد."));
            if (!Editable(row)) return Result.Failure<MeetingDto>(Error.Forbidden("ویرایش جلسه فقط برای ثبت‌کننده یا برگزارکننده مجاز است."));
            if (input.Version != row.Version) return Result.Failure<MeetingDto>(Error.Conflict("جلسه تغییر کرده است؛ فهرست را تازه کنید و دوباره ویرایش کنید."));
            before = JsonSerializer.Deserialize<MeetingInput>(row.Content, Json);
        }
        if (await ValidateAsync(input, row is null || row.Date != input.Date, ct) is { } problem) return Result.Failure<MeetingDto>(Error.Validation(problem));
        var scope = await peopleScope.GetAsync(ct);
        var allowed = scope.People.Select(p => p.Id).ToHashSet();
        var referenced = input.ParticipantIds.Append(input.OrganizerUserId).Append(current.UserId.Value)
            .Concat(input.Agenda.Select(a => a.PresenterUserId).OfType<Guid>())
            .Concat(input.Actions.Select(a => a.AssigneeUserId).OfType<Guid>());
        if (row is not null) referenced = referenced.Append(row.CreatedByUserId);
        if (referenced.Any(person => !allowed.Contains(person)))
            return Result.Failure<MeetingDto>(Error.Forbidden(scope.Message ?? "یکی از افراد جلسه خارج از زنجیره و محدوده مجاز شماست؛ دعوت از بالادست به دسترسی مخصوص جلسه نیاز دارد."));
        var clean = input with { Title = input.Title.Trim(), ParticipantIds = input.ParticipantIds.Distinct().ToArray(), TaskIds = input.TaskIds.Distinct().ToArray(), Version = null };
        if (await SchedulingConflictAsync(id, clean, ct) is { } conflict)
            return Result.Failure<MeetingDto>(Error.Conflict(conflict));
        if (row is null)
        {
            row = new MeetingRecord { Id = Guid.NewGuid(), TenantId = current.TenantId.Value, CreatedByUserId = current.UserId.Value };
            db.Meetings.Add(row);
        }
        else row.Version++;
        row.OrganizerUserId = clean.OrganizerUserId; row.Date = clean.Date; row.Content = JsonSerializer.Serialize(clean, Json);
        foreach (var old in row.Participants.Where(p => !clean.ParticipantIds.Contains(p.UserId)).ToList()) row.Participants.Remove(old);
        foreach (var user in clean.ParticipantIds.Where(id => row.Participants.All(p => p.UserId != id))) row.Participants.Add(new() { MeetingId = row.Id, UserId = user });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<MeetingDto>(Error.Conflict("جلسه هم‌زمان تغییر کرده است؛ فهرست را تازه کنید.")); }
        if (transaction is not null) await transaction.CommitAsync(ct);
        var notice = await InviteAsync(clean, before);
        return Result.Success(Dto(row, notice));
    }

    private async Task<string?> InviteAsync(MeetingInput meeting, MeetingInput? before)
    {
        if (!meeting.SendInvitationSms || meeting.ParticipantIds.Count == 0) return null;
        var changed = before is null || !before.SendInvitationSms || before.Date != meeting.Date || before.StartTime != meeting.StartTime
            || before.EndTime != meeting.EndTime || before.Title != meeting.Title || before.Location != meeting.Location;
        var recipients = meeting.ParticipantIds.Where(id => changed || !before!.ParticipantIds.Contains(id)).ToList();
        if (recipients.Count == 0) return null;
        using var invitationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (!await sms.IsEnabledAsync(current.TenantId!.Value, SmsTemplateKeys.MeetingNotice, invitationTimeout.Token))
                return "جلسه ذخیره شد؛ قالب پیامک اطلاع‌رسانی جلسه غیرفعال است.";
            var people = await identity.Users.Where(u => u.TenantId == current.TenantId && recipients.Contains(u.Id)).ToListAsync(invitationTimeout.Token);
            var failed = 0;
            var date = new PersianCalendar();
            var day = meeting.Date.ToDateTime(TimeOnly.MinValue);
            foreach (var person in people)
            {
                if (string.IsNullOrWhiteSpace(person.PhoneNumber) || !person.NotifySms) { failed++; continue; }
                var result = await sms.SendAsync(current.TenantId!.Value, person.PhoneNumber, SmsTemplateKeys.MeetingNotice,
                    new Dictionary<string, string?> { ["recipient"] = person.DisplayName, ["title"] = meeting.Title,
                        ["date"] = $"{date.GetYear(day)}/{date.GetMonth(day):00}/{date.GetDayOfMonth(day):00}", ["time"] = meeting.StartTime, ["location"] = meeting.Location ?? "—" }, invitationTimeout.Token);
                if (result.IsFailure) failed++;
            }
            return failed > 0 ? "جلسه ذخیره شد؛ پیامک بعضی افراد ارسال نشد. تنظیمات پنل، شماره موبایل و ترجیح دریافت پیامک را بررسی کنید." : "جلسه ذخیره شد و درخواست ارسال دعوت‌نامه انجام شد.";
        }
        catch (Exception ex) { logger.LogWarning(ex, "Meeting saved, but invitation SMS failed"); return "جلسه ذخیره شد؛ ارسال پیامک انجام نشد."; }
    }

    public async Task<Result> DeleteAsync(Guid id, int version, CancellationToken ct)
    {
        if (!current.HasPermission(MeetingPermissions.Edit)) return Result.Failure(Error.Forbidden());
        var row = await db.Meetings.Include(m => m.Participants).FirstOrDefaultAsync(m => m.TenantId == current.TenantId && m.Id == id, ct);
        if (row is null) return Result.Failure(Error.NotFound("جلسه پیدا نشد."));
        var visible = await VisiblePeopleAsync(ct);
        if (!AccessAll && !Touches(row, visible)) return Result.Failure(Error.NotFound("جلسه پیدا نشد."));
        if (!Editable(row)) return Result.Failure(Error.Forbidden());
        if (row.Version != version) return Result.Failure(Error.Conflict("جلسه تغییر کرده است؛ فهرست را تازه کنید."));
        db.Meetings.Remove(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result.Failure(Error.Conflict("جلسه هم‌زمان تغییر کرده است.")); }
        return Result.Success();
    }
}

public static class MeetingModule
{
    public static IServiceCollection AddCalendarMeetings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MeetingDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        services.AddSingleton<IPermissionCatalog, CalendarPermissionCatalog>();
services.AddAuthorization(options =>
{
    foreach (var permission in CalendarPermissions.All)
    {
        options.AddPolicy(
            permission.Name,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new PermissionRequirement(permission.Name)
                )
        );
    }
});
        services.AddScoped<IMeetingPeopleScope, UnavailableMeetingPeopleScope>();
        services.AddScoped<IMeetingTaskDirectory, UnavailableMeetingTaskDirectory>();
        services.AddScoped<MeetingService>(); return services;
    }
    public static void MapMeetingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar/meetings").WithTags("Meetings").RequireAuthorization();
        group.MapGet("/people-options", async (MeetingService service, CancellationToken ct) =>
            (await service.PeopleAsync(ct)).ToApiResult());
        group.MapGet("/task-options", async (string? search, int? pageNumber, int? pageSize, string? ids, MeetingService service, CancellationToken ct) =>
            (await service.SearchTasksAsync(search,pageNumber ?? 1,pageSize ?? 12,ids,ct)).ToApiResult()).RequireAuthorization(MeetingPermissions.View).RequireAuthorization("Tasks.View");
        group.MapGet("/", async (DateOnly from, DateOnly to, MeetingService service, CancellationToken ct) =>
            (await service.ListAsync(from, to, ct)).ToApiResult()).RequireAuthorization(MeetingPermissions.View);
        group.MapPost("/", async (MeetingInput input, MeetingService service, CancellationToken ct) =>
            (await service.SaveAsync(null, input, ct)).ToApiResult()).RequireAuthorization(MeetingPermissions.Create);
        group.MapPut("/{id:guid}", async (Guid id, MeetingInput input, MeetingService service, CancellationToken ct) =>
            (await service.SaveAsync(id, input, ct)).ToApiResult()).RequireAuthorization(MeetingPermissions.Edit);
        group.MapDelete("/{id:guid}", async (Guid id, int version, MeetingService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, version, ct)).ToApiResult()).RequireAuthorization(MeetingPermissions.Edit);
    }
}

internal sealed class UnavailableMeetingTaskDirectory : IMeetingTaskDirectory
{
    public Task<Result<MeetingTaskPage>> SearchAsync(string? search, int pageNumber, int pageSize, string? ids,
        CancellationToken cancellationToken) => Task.FromResult(Result.Failure<MeetingTaskPage>(
        Error.NotFound("فهرست وظایف در این میزبان فعال نیست.")));

    public Task<bool> CanAccessAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(false);
}

internal sealed class UnavailableMeetingPeopleScope(ICurrentUserContext current) : IMeetingPeopleScope
{
    public Task<MeetingPeopleOptions> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
        new MeetingPeopleOptions(false, false, [],
            current.UserId is null ? "ابتدا وارد سامانه شوید." : "اتصال چارت سازمانی جلسات در این میزبان فعال نیست."));
}
