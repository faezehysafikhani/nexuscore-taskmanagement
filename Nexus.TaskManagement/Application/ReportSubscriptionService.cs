using System.Globalization;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>Report distribution (CAT-195): a person asks for a digest of their own tasks on a schedule, by SMS and/or email. Only their own - nobody subscribes someone else.</summary>
public sealed class ReportSubscriptionService(
    IReportSubscriptionRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    TimeProvider clock) : IReportSubscriptionService
{
    public const int MaxPerUser = 5;

    private static ReportSubscriptionDto ToDto(ReportSubscription s) =>
        new(s.Id, s.Frequency, s.DayOfWeek, s.DayOfMonth, s.Hour, s.BySms, s.ByEmail, s.IsActive, s.NextRunAtUtc, s.LastSentAtUtc);

    private static Error? Check(SaveReportSubscriptionRequest r) =>
        !Enum.IsDefined(r.Frequency) || r.Hour is < 0 or > 23 || r.DayOfMonth is < 1 or > 28 || (r.DayOfWeek is { } d && !Enum.IsDefined(d))
            ? Error.Validation("Frequency, hour (0-23) or day is not valid.")
            : !r.BySms && !r.ByEmail ? Error.Validation("Choose SMS, email or both.") : null;

    public async Task<Result<IReadOnlyList<ReportSubscriptionDto>>> ListAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenant || currentUser.UserId is not { } user
            ? Result.Failure<IReadOnlyList<ReportSubscriptionDto>>(Error.Unauthorized())
            : Result.Success<IReadOnlyList<ReportSubscriptionDto>>((await repository.ListOfUserAsync(tenant, user, cancellationToken)).Select(ToDto).ToList());

    public async Task<Result<ReportSubscriptionDto>> CreateAsync(SaveReportSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenant || currentUser.UserId is not { } user)
        {
            return Result.Failure<ReportSubscriptionDto>(Error.Unauthorized());
        }

        if (Check(request) is { } invalid)
        {
            return Result.Failure<ReportSubscriptionDto>(invalid);
        }

        if ((await repository.ListOfUserAsync(tenant, user, cancellationToken)).Count >= MaxPerUser)
        {
            return Result.Failure<ReportSubscriptionDto>(Error.Validation($"At most {MaxPerUser} report schedules."));
        }

        var s = new ReportSubscription(Guid.NewGuid(), tenant, user, request.Frequency, request.DayOfWeek, request.DayOfMonth, request.Hour, request.BySms, request.ByEmail, clock.GetUtcNow());
        if (!request.IsActive)
        {
            s.SetActive(false, clock.GetUtcNow());
        }

        await repository.AddAsync(s, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(s));
    }

    public async Task<Result<ReportSubscriptionDto>> UpdateAsync(Guid id, SaveReportSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenant || currentUser.UserId is not { } user)
        {
            return Result.Failure<ReportSubscriptionDto>(Error.Unauthorized());
        }

        var s = await repository.GetAsync(tenant, user, id, cancellationToken);
        if (s is null)
        {
            return Result.Failure<ReportSubscriptionDto>(Error.NotFound("Report schedule not found."));
        }

        if (Check(request) is { } invalid)
        {
            return Result.Failure<ReportSubscriptionDto>(invalid);
        }

        s.Set(request.Frequency, request.DayOfWeek, request.DayOfMonth, request.Hour, request.BySms, request.ByEmail, clock.GetUtcNow());
        s.SetActive(request.IsActive, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(s));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenant || currentUser.UserId is not { } user)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var s = await repository.GetAsync(tenant, user, id, cancellationToken);
        if (s is null)
        {
            return Result.Failure(Error.NotFound("Report schedule not found."));
        }

        repository.Remove(s);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>The words of a digest (Persian, with the Persian date), short enough for one SMS.</summary>
public static class TaskDigestText
{
    private static readonly PersianCalendar Persian = new();

    public static string Date(DateOnly day)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetYear(d)}/{Persian.GetMonth(d):00}/{Persian.GetDayOfMonth(d):00}";
    }

    public static string Subject(DateOnly today) => $"خلاصه وظایف شما - {Date(today)}";

    public static string Sms(DateOnly today, TaskDigest d) =>
        $"{Subject(today)}\nعقب‌افتاده: {d.Overdue} | امروز: {d.DueToday} | ۷ روز آینده: {d.DueSoon} | در حال انجام: {d.InProgress} | انجام‌شده هفته‌ی گذشته: {d.CompletedLastWeek}"
        + (d.OverdueTitles.Count > 0 ? $"\nعقب‌افتاده‌ها: {string.Join("، ", d.OverdueTitles)}" : string.Empty);

    public static string Email(DateOnly today, TaskDigest d) =>
        $"{Subject(today)}\n\nعقب‌افتاده: {d.Overdue}\nسررسید امروز: {d.DueToday}\nسررسید تا ۷ روز آینده: {d.DueSoon}\nدر حال انجام: {d.InProgress}\nانجام‌شده در هفته‌ی گذشته: {d.CompletedLastWeek}"
        + (d.OverdueTitles.Count > 0 ? "\n\nوظایف عقب‌افتاده:\n- " + string.Join("\n- ", d.OverdueTitles) : string.Empty);
}
