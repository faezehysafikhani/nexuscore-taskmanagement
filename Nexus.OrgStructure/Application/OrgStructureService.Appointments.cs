using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Appointments over time (phase 9): a post's holder is appointed from a day and ends on a day; the
/// past is never removed or rewritten. Everything that asks who holds a post sees only the
/// appointment on now (IOrgStructureRepository.ListUserPositionsAsync), so for today's data nothing
/// changes. One post has one person at a time; one person may hold several posts, as before.
/// </summary>
public interface IOrgAppointmentService
{
    /// <summary>Gives the post to someone (now or from a later day), ending the present holder's appointment then, in one save.</summary>
    Task<Result<AppointmentDto>> AppointAsync(Guid tenantId, Guid unitPositionId, AppointRequest request, CancellationToken cancellationToken);

    Task<Result<AppointmentDto>> EndAppointmentAsync(Guid tenantId, Guid unitPositionId, EndAppointmentRequest request, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AppointmentDto>>> GetPostHistoryAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AppointmentDto>>> GetUserHistoryAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}

public sealed partial class OrgStructureService : IOrgAppointmentService
{
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    /// <summary>A minute's slack: «now» sent by a client is a little in the past by the time it arrives.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(1);

    private async Task<IReadOnlyList<UserPosition>> UpcomingAppointmentsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = Now;
        return (await repository.ListUserPositionHistoryAsync(tenantId, null, null, cancellationToken)).Where(a => a.StartDate > now).ToList();
    }

    /// <summary>
    /// Appointments of one organization go one at a time (the organization's turn, as the node changes do), so two
    /// requests for one post never race to the unique «one open appointment per post» index: the second one sees
    /// what the first did. Without a lock registered (some tests) it is not taken.
    /// </summary>
    private async Task<(IAsyncDisposable? Turn, Error? Busy)> AppointmentTurnAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (writeLock is null)
        {
            return (null, null);
        }

        var turn = await writeLock.BeginAsync(tenantId, cancellationToken);
        return turn is null
            ? (null, Error.Conflict("ساختار سازمانی همین حالا در حال تغییر است؛ چند لحظه بعد دوباره امتحان کنید."))
            : (turn, null);
    }

    private static Error ChangedMeanwhile() => Error.Conflict("این پست را کس دیگری همین حالا تغییر داده است؛ چارت را دوباره بارگذاری کنید و دوباره امتحان کنید.");

    public async Task<Result<AppointmentDto>> AppointAsync(Guid tenantId, Guid unitPositionId, AppointRequest request, CancellationToken cancellationToken)
    {
        var (turn, busy) = await AppointmentTurnAsync(tenantId, cancellationToken);
        if (busy is not null)
        {
            return Result.Failure<AppointmentDto>(busy);
        }

        await using var _ = turn;
        var now = Now;
        var start = request.StartDate ?? now;
        if (start < now - Slack)
        {
            return Result.Failure<AppointmentDto>(Error.Validation("انتصاب از تاریخ گذشته ثبت نمی‌شود؛ سابقه بازنویسی نمی‌شود."));
        }

        start = start < now ? now : start;
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null || !snapshot.IsShown(seat) || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            return Result.Failure<AppointmentDto>(Error.NotFound("پست پیدا نشد."));
        }

        var person = (await users.GetUsersAsync([request.UserId], cancellationToken)).FirstOrDefault();
        if (person is null || person.TenantId != tenantId || !person.IsActive)
        {
            return Result.Failure<AppointmentDto>(Error.Validation("فرد باید از کاربران فعال همین سازمان باشد."));
        }

        if (snapshot.Assignments.FirstOrDefault(a => a.UserId == request.UserId) is not { } place || place.CompanyId != seat.CompanyId)
        {
            return Result.Failure<AppointmentDto>(Error.Validation("فرد اول باید در شرکت همین پست جایگاه داشته باشد."));
        }

        if (snapshot.Positions.GetValueOrDefault(seat.PositionId) is not { IsActive: true })
        {
            return Result.Failure<AppointmentDto>(Error.Validation("سمت این پست غیرفعال است."));
        }

        var history = await repository.ListUserPositionHistoryAsync(tenantId, null, unitPositionId, cancellationToken);
        if (history.Any(a => a.StartDate > now))
        {
            return Result.Failure<AppointmentDto>(Error.Conflict("این پست یک انتصاب آینده دارد؛ اول آن را لغو کنید."));
        }

        // Whoever is on it then (open, or ending after the new start) ends when the new one starts.
        var current = history.Where(a => a.IsActiveAt(now) && (a.EndDate is not { } end || end > start)).ToList();
        if (current.Any(a => a.UserId == request.UserId && a.EndDate is null))
        {
            return Result.Failure<AppointmentDto>(Error.Conflict("این فرد همین حالا این پست را دارد."));
        }

        // Taking it now must not make a management loop (a later start is checked again when it starts).
        if (start <= now)
        {
            var holdings = snapshot.Holdings.Where(h => h.UnitPositionId != unitPositionId)
                .Append(new UserPosition(Guid.Empty, tenantId, request.UserId, unitPositionId)).ToList();
            if (snapshot.Resolver().FindLoop() is null && (snapshot with { Holdings = holdings }).Resolver().FindLoop() is not null)
            {
                return Result.Failure<AppointmentDto>(Error.Conflict("این انتصاب حلقه‌ی مدیریتی می‌سازد."));
            }
        }

        foreach (var appointment in current)
        {
            appointment.End(start);
        }

        var added = new UserPosition(Guid.NewGuid(), tenantId, request.UserId, unitPositionId, start);
        await repository.AddUserPositionAsync(added, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Another server won the unique index between our read and our save (the turn is per server without the SQL lock).
            return Result.Failure<AppointmentDto>(ChangedMeanwhile());
        }

        // Open tasks of the post go to its new holder (now; a later start: when it starts).
        if (start <= now && taskPosts is not null)
        {
            await taskPosts.SyncHoldersAsync(tenantId, [unitPositionId], cancellationToken);
        }

        return Result.Success(await AppointmentDtoAsync(tenantId, snapshot, added, cancellationToken));
    }

    public async Task<Result<AppointmentDto>> EndAppointmentAsync(Guid tenantId, Guid unitPositionId, EndAppointmentRequest request, CancellationToken cancellationToken)
    {
        var (turn, busy) = await AppointmentTurnAsync(tenantId, cancellationToken);
        if (busy is not null)
        {
            return Result.Failure<AppointmentDto>(busy);
        }

        await using var _ = turn;
        var now = Now;
        var end = request.EndDate ?? now;
        if (end < now - Slack)
        {
            return Result.Failure<AppointmentDto>(Error.Validation("پایان انتصاب در گذشته ثبت نمی‌شود؛ سابقه بازنویسی نمی‌شود."));
        }

        end = end < now ? now : end;
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            return Result.Failure<AppointmentDto>(Error.NotFound("پست پیدا نشد."));
        }

        var history = await repository.ListUserPositionHistoryAsync(tenantId, null, unitPositionId, cancellationToken);
        if (history.FirstOrDefault(a => a.IsActiveAt(now)) is not { } current)
        {
            return Result.Failure<AppointmentDto>(Error.NotFound("این پست الان دارنده‌ای ندارد."));
        }

        if (history.Any(a => a.StartDate > now))
        {
            return Result.Failure<AppointmentDto>(Error.Conflict("این پست یک انتصاب آینده دارد؛ پایان فعلی با شروع آن تعیین شده است."));
        }

        current.End(end);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (end <= now && taskPosts is not null)
        {
            await taskPosts.SyncHoldersAsync(tenantId, [unitPositionId], cancellationToken);
        }

        return Result.Success(await AppointmentDtoAsync(tenantId, snapshot, current, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> GetPostHistoryAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<AppointmentDto>>(Error.NotFound("پست پیدا نشد."));
        }

        return Result.Success(await AppointmentDtosAsync(tenantId, snapshot, await repository.ListUserPositionHistoryAsync(tenantId, null, unitPositionId, cancellationToken), cancellationToken));
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> GetUserHistoryAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var rows = (await repository.ListUserPositionHistoryAsync(tenantId, userId, null, cancellationToken))
            .Where(a => mine is null || snapshot.Seats.FirstOrDefault(s => s.Id == a.UnitPositionId) is not { } seat || mine.Contains(seat.CompanyId))
            .ToList();
        return Result.Success(await AppointmentDtosAsync(tenantId, snapshot, rows, cancellationToken));
    }

    private async Task<AppointmentDto> AppointmentDtoAsync(Guid tenantId, Snapshot snapshot, UserPosition appointment, CancellationToken cancellationToken) =>
        (await AppointmentDtosAsync(tenantId, snapshot, [appointment], cancellationToken))[0];

    /// <summary>Newest first. A post removed since is still listed (its title and place unknown).</summary>
    private async Task<IReadOnlyList<AppointmentDto>> AppointmentDtosAsync(Guid tenantId, Snapshot snapshot, IReadOnlyList<UserPosition> rows, CancellationToken cancellationToken)
    {
        var now = Now;
        var people = await PeopleAsync(rows.Select(a => (Guid?)a.UserId), cancellationToken);
        return rows
            .OrderByDescending(a => a.StartDate ?? DateTimeOffset.MinValue)
            .Select(a =>
            {
                var seat = snapshot.Seats.FirstOrDefault(s => s.Id == a.UnitPositionId);
                return new AppointmentDto(
                    a.Id, a.UnitPositionId,
                    seat is null ? null : snapshot.Positions.GetValueOrDefault(seat.PositionId)?.Title,
                    seat?.Code,
                    seat is null ? null : snapshot.PlaceOf(seat),
                    Person(a.UserId, people),
                    a.StartDate, a.EndDate, a.IsActiveAt(now));
            })
            .ToList();
    }
}
