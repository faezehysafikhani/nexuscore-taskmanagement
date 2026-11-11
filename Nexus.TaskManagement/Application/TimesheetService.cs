using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Working hours (phase 6): entries and a timer on tasks, an estimate against the actual, a week sent for approval
/// through the approval backend (<see cref="IApprovalRequester"/>; none set up means the week is approved at once), and
/// the workload of the people. Time is minutes; the week opens on Saturday.
/// </summary>
public sealed class TimesheetService(
    ITimesheetRepository repository,
    ITaskRepository taskRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ITaskAccessScope access,
    IApprovalRequester approvalRequester,
    NexusCore.Application.Identity.Interfaces.IManagementChainProvider? managers = null) : ITimesheetService
{
    public const string SubjectType = "Timesheet";
    public const int MaxMinutesPerDay = 24 * 60;
    public const int DefaultWeeklyCapacityMinutes = 44 * 60;

    /// <summary>The Saturday on or before the day.</summary>
    public static DateOnly WeekStartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 1) % 7));

    // --- estimate and per-task time ---

    public async Task<Result<TaskTimeDto>> GetTaskTimeAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskTimeDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskTimeDto>(Error.NotFound("Task not found."));
        }

        return Result.Success(await TaskTimeAsync(task, cancellationToken));
    }

    private async Task<TaskTimeDto> TaskTimeAsync(TaskItem task, CancellationToken cancellationToken)
    {
        // Everybody who sees the task sees the total; the entries are shown to the owner and to time managers.
        var entries = await repository.ListEntriesForTaskAsync(task.TenantId, task.Id, cancellationToken);
        var seeAll = access.CanManage(task) || currentUser.HasPermission(TaskManagementPermissions.ViewAllTime);
        var shown = entries.Where(e => seeAll || e.UserId == currentUser.UserId).ToList();
        return new TaskTimeDto(task.EstimatedMinutes, entries.Sum(e => e.Minutes), await ToDtosAsync(shown, cancellationToken));
    }

    public async Task<Result<TaskTimeDto>> SetEstimateAsync(Guid taskId, SetEstimateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskTimeDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskTimeDto>(Error.NotFound("Task not found."));
        }

        if (!(access.CanManage(task) || (currentUser.UserId is { } who && task.IsResponsible(who))))
        {
            return Result.Failure<TaskTimeDto>(Error.Forbidden("Only the task owner or the person doing it can estimate it."));
        }

        if (request.Minutes is <= 0 or > 1_000_000)
        {
            return Result.Failure<TaskTimeDto>(Error.Validation("The estimate must be a positive number of minutes, or empty."));
        }

        task.SetEstimate(request.Minutes);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await TaskTimeAsync(task, cancellationToken));
    }

    // --- entries ---

    public async Task<Result<TimeEntryDto>> LogAsync(LogTimeRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimeEntryDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, request.TaskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TimeEntryDto>(Error.NotFound("Task not found."));
        }

        var invalid = await ValidateAsync(tenantId, userId, request.Date, request.Minutes, null, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<TimeEntryDto>(invalid);
        }

        var entry = new TimeEntry(Guid.NewGuid(), tenantId, task.Id, userId, request.Date, request.Minutes, Clean(request.Note), TimeEntrySource.Manual);
        await repository.AddEntryAsync(entry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success((await ToDtosAsync([entry], cancellationToken))[0]);
    }

    public async Task<Result<TimeEntryDto>> UpdateAsync(Guid id, UpdateTimeEntryRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimeEntryDto>(Error.Unauthorized());
        }

        var entry = await repository.GetEntryAsync(tenantId, id, cancellationToken);
        if (entry is null || entry.UserId != userId)
        {
            return Result.Failure<TimeEntryDto>(Error.NotFound("Time entry not found."));
        }

        if (entry.IsLocked)
        {
            return Result.Failure<TimeEntryDto>(Error.Conflict("This entry is already sent for approval and cannot be changed."));
        }

        var invalid = await ValidateAsync(tenantId, userId, request.Date, request.Minutes, entry.Id, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<TimeEntryDto>(invalid);
        }

        entry.Update(request.Date, request.Minutes, Clean(request.Note));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success((await ToDtosAsync([entry], cancellationToken))[0]);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var entry = await repository.GetEntryAsync(tenantId, id, cancellationToken);
        if (entry is null || entry.UserId != userId)
        {
            return Result.Failure(Error.NotFound("Time entry not found."));
        }

        if (entry.IsLocked)
        {
            return Result.Failure(Error.Conflict("This entry is already sent for approval and cannot be removed."));
        }

        repository.RemoveEntry(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateAsync(Guid tenantId, Guid userId, DateOnly date, int minutes, Guid? exceptEntryId, CancellationToken cancellationToken)
    {
        if (minutes is < 1 or > MaxMinutesPerDay)
        {
            return Error.Validation("Minutes must be between 1 and 1440.");
        }

        if (date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
        {
            return Error.Validation("Time cannot be logged for a future day.");
        }

        if (await repository.SumMinutesOnDayAsync(tenantId, userId, date, exceptEntryId, cancellationToken) + minutes > MaxMinutesPerDay)
        {
            return Error.Validation("A day has only 24 hours.");
        }

        return null;
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    // --- timer ---

    public async Task<Result<TimerDto?>> GetTimerAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimerDto?>(Error.Unauthorized());
        }

        var timer = await repository.GetTimerAsync(userId, cancellationToken);
        return Result.Success(timer is null ? null : await ToDtoAsync(timer, cancellationToken));
    }

    public async Task<Result<TimerDto>> StartTimerAsync(StartTimerRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimerDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, request.TaskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TimerDto>(Error.NotFound("Task not found."));
        }

        // Starting another one stops the running one first, so no time is lost or counted twice.
        var running = await repository.GetTimerAsync(userId, cancellationToken);
        if (running is not null)
        {
            if (running.TaskId == task.Id)
            {
                return Result.Failure<TimerDto>(Error.Conflict("The timer is already running on this task."));
            }

            var stopped = await StopAsync(running, tenantId, userId, cancellationToken);
            if (stopped.IsFailure)
            {
                return Result.Failure<TimerDto>(stopped.Error);
            }
        }

        var timer = new RunningTimer(userId, tenantId, task.Id, DateTimeOffset.UtcNow);
        await repository.AddTimerAsync(timer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(timer, cancellationToken));
    }

    public async Task<Result<TimeEntryDto>> StopTimerAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimeEntryDto>(Error.Unauthorized());
        }

        var running = await repository.GetTimerAsync(userId, cancellationToken);
        return running is null
            ? Result.Failure<TimeEntryDto>(Error.NotFound("No timer is running."))
            : await StopAsync(running, tenantId, userId, cancellationToken);
    }

    private async Task<Result<TimeEntryDto>> StopAsync(RunningTimer timer, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var minutes = Math.Clamp((int)Math.Ceiling((now - timer.StartedAtUtc).TotalMinutes), 1, MaxMinutesPerDay);
        var date = DateOnly.FromDateTime(timer.StartedAtUtc.UtcDateTime);

        // A timer left running overnight cannot overfill the day: it is cut to what the day has left.
        var left = MaxMinutesPerDay - await repository.SumMinutesOnDayAsync(tenantId, userId, date, null, cancellationToken);
        repository.RemoveTimer(timer);
        if (left <= 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<TimeEntryDto>(Error.Validation("That day is already full; the timer was discarded."));
        }

        var entry = new TimeEntry(Guid.NewGuid(), tenantId, timer.TaskId, userId, date, Math.Min(minutes, left), null, TimeEntrySource.Timer);
        await repository.AddEntryAsync(entry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success((await ToDtosAsync([entry], cancellationToken))[0]);
    }

    private async Task<TimerDto> ToDtoAsync(RunningTimer timer, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetByIdAsync(timer.TenantId, timer.TaskId, cancellationToken);
        return new TimerDto(timer.TaskId, task?.Title, timer.StartedAtUtc, (int)(DateTimeOffset.UtcNow - timer.StartedAtUtc).TotalMinutes);
    }

    // --- weeks ---

    public async Task<Result<TimesheetWeekDto>> GetWeekAsync(DateOnly anyDayOfWeek, Guid? userId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } me)
        {
            return Result.Failure<TimesheetWeekDto>(Error.Unauthorized());
        }

        var target = userId ?? me;
        if (target != me && !currentUser.HasPermission(TaskManagementPermissions.ViewAllTime))
        {
            return Result.Failure<TimesheetWeekDto>(Error.Forbidden("Only a time manager can see another person's timesheet."));
        }

        var start = WeekStartOf(anyDayOfWeek);
        var entries = await repository.ListEntriesAsync(tenantId, target, start, start.AddDays(6), cancellationToken);
        var submission = await repository.FindSubmissionAsync(tenantId, target, start, cancellationToken);
        var perDay = entries.GroupBy(e => e.Date).ToDictionary(g => g.Key.ToString("yyyy-MM-dd"), g => g.Sum(e => e.Minutes));

        // The week's state: pending/approved while it is with an approver; otherwise what the last decision left.
        var status = entries.Count == 0 ? ApprovalStatus.NotSubmitted
            : entries.Any(e => e.Status == ApprovalStatus.PendingApproval) ? ApprovalStatus.PendingApproval
            : entries.Any(e => e.Status == ApprovalStatus.Rejected) ? ApprovalStatus.Rejected
            : entries.All(e => e.Status == ApprovalStatus.Approved) ? ApprovalStatus.Approved
            : ApprovalStatus.NotSubmitted;
        var canSubmit = target == me && entries.Any(e => e.Status is ApprovalStatus.NotSubmitted or ApprovalStatus.Rejected)
            && !entries.Any(e => e.Status == ApprovalStatus.PendingApproval);
        return Result.Success(new TimesheetWeekDto(
            start, await ToDtosAsync(entries, cancellationToken), perDay, entries.Sum(e => e.Minutes), status,
            submission?.Id, submission?.DecisionComment, canSubmit));
    }

    public async Task<Result<TimesheetWeekDto>> SubmitWeekAsync(SubmitTimesheetRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TimesheetWeekDto>(Error.Unauthorized());
        }

        var start = WeekStartOf(request.WeekStart);
        var entries = await repository.ListEntriesAsync(tenantId, userId, start, start.AddDays(6), cancellationToken);
        if (entries.Any(e => e.Status == ApprovalStatus.PendingApproval))
        {
            return Result.Failure<TimesheetWeekDto>(Error.Conflict("This week is already waiting for approval."));
        }

        var toSend = entries.Where(e => e.Status is ApprovalStatus.NotSubmitted or ApprovalStatus.Rejected).ToList();
        if (toSend.Count == 0)
        {
            return Result.Failure<TimesheetWeekDto>(Error.Validation("There is no time to send for this week."));
        }

        var now = DateTimeOffset.UtcNow;
        var submission = new TimesheetSubmission(Guid.NewGuid(), tenantId, userId, start, toSend.Sum(e => e.Minutes), now);
        await repository.AddSubmissionAsync(submission, cancellationToken);
        foreach (var entry in toSend)
        {
            entry.MarkSubmitted(submission.Id);
        }

        // Owner is the person; Manager and Approver1/2 their managers in the organization structure (when installed).
        var participants = new Dictionary<string, Guid> { [ApprovalParticipants.Owner] = userId };
        if (managers is not null)
        {
            var chain = await managers.GetChainAsync(tenantId, userId, cancellationToken);
            if (chain.Count > 0)
            {
                participants[ApprovalParticipants.Manager] = chain[0];
                participants[ApprovalParticipants.Approver1] = chain[0];
            }

            if (chain.Count > 1)
            {
                participants[ApprovalParticipants.Approver2] = chain[1];
            }
        }

        var outcome = await approvalRequester.RequestApprovalAsync(
            new ApprovalSubject(SubjectType, submission.Id, tenantId, Participants: participants), cancellationToken);
        if (outcome == ApprovalRequestOutcome.NotConfigured)
        {
            // No approval chain: nobody to wait for.
            submission.Decide(true, userId, null, now);
            foreach (var entry in toSend)
            {
                entry.SetDecision(true);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetWeekAsync(start, null, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<TimesheetReviewItemDto>>> ListInReviewAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<TimesheetReviewItemDto>>(Error.Unauthorized());
        }

        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<TimesheetReviewItemDto>>([]);
        }

        var waiting = await repository.GetPendingSubmissionsAsync(tenantId, ids.Distinct().Take(200).ToList(), cancellationToken);
        var people = await taskRepository.GetUserSummariesAsync(waiting.Select(s => s.UserId).Distinct().ToList(), cancellationToken);
        return Result.Success<IReadOnlyList<TimesheetReviewItemDto>>(waiting.Select(s => new TimesheetReviewItemDto(
            s.Id, people.GetValueOrDefault(s.UserId), s.WeekStart, s.TotalMinutes, s.SubmittedAtUtc)).ToList());
    }

    // --- workload ---

    public async Task<Result<WorkloadDto>> GetWorkloadAsync(DateOnly? from, DateOnly? to, int? capacityMinutes, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<WorkloadDto>(Error.Unauthorized());
        }

        if (!currentUser.HasPermission(TaskManagementPermissions.ViewAllTime))
        {
            return Result.Failure<WorkloadDto>(Error.Forbidden("Only a time manager can see the workload."));
        }

        var start = from ?? WeekStartOf(DateOnly.FromDateTime(DateTime.UtcNow));
        var end = to ?? start.AddDays(6);
        if (end < start || end.DayNumber - start.DayNumber > 366)
        {
            return Result.Failure<WorkloadDto>(Error.Validation("Choose a range of at most a year."));
        }

        // Capacity is per week; scale it to the range.
        var weeks = Math.Max(1.0, (end.DayNumber - start.DayNumber + 1) / 7.0);
        var capacity = (int)Math.Round((capacityMinutes ?? DefaultWeeklyCapacityMinutes) * weeks);
        var rows = await repository.WorkloadAsync(tenantId, start, end, cancellationToken);
        var people = await taskRepository.GetUserSummariesAsync(rows.Select(r => r.UserId).ToList(), cancellationToken);
        return Result.Success(new WorkloadDto(start, end, capacity, rows
            .Where(r => people.ContainsKey(r.UserId))
            .Select(r => new WorkloadRowDto(
                people[r.UserId], r.Logged, r.OpenTasks, r.RemainingEstimated, capacity,
                capacity == 0 ? 0 : Math.Round((double)(r.Logged + r.RemainingEstimated) / capacity, 2)))
            .OrderByDescending(r => r.Utilization).ToList()));
    }

    private async Task<IReadOnlyList<TimeEntryDto>> ToDtosAsync(IReadOnlyList<TimeEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var tenantId = entries[0].TenantId;
        var titles = new Dictionary<Guid, string>();
        foreach (var taskId in entries.Select(e => e.TaskId).Distinct())
        {
            var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
            if (task is not null)
            {
                titles[taskId] = task.Title;
            }
        }

        var people = await taskRepository.GetUserSummariesAsync(entries.Select(e => e.UserId).Distinct().ToList(), cancellationToken);
        return entries.Select(e => new TimeEntryDto(
            e.Id, e.TaskId, titles.GetValueOrDefault(e.TaskId), e.UserId, people.GetValueOrDefault(e.UserId)?.DisplayName,
            e.Date, e.Minutes, e.Note, e.Source, e.Status)).ToList();
    }
}

/// <summary>The week was approved: its entries are final.</summary>
public sealed class TimesheetApprovedHandler(ITimesheetRepository repository, ITaskManagementUnitOfWork unitOfWork) : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != TimesheetService.SubjectType)
        {
            return;
        }

        await TimesheetDecision.ApplyAsync(repository, unitOfWork, domainEvent.TenantId, domainEvent.SubjectId, true, domainEvent.DecidedByUserId, domainEvent.Comment, cancellationToken);
    }
}

/// <summary>The week was rejected: its entries open up again for correction and a new submission.</summary>
public sealed class TimesheetRejectedHandler(ITimesheetRepository repository, ITaskManagementUnitOfWork unitOfWork) : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != TimesheetService.SubjectType)
        {
            return;
        }

        await TimesheetDecision.ApplyAsync(repository, unitOfWork, domainEvent.TenantId, domainEvent.SubjectId, false, domainEvent.DecidedByUserId, domainEvent.Comment, cancellationToken);
    }
}

internal static class TimesheetDecision
{
    public static async Task ApplyAsync(
        ITimesheetRepository repository, ITaskManagementUnitOfWork unitOfWork, Guid tenantId, Guid submissionId,
        bool approved, Guid? byUserId, string? comment, CancellationToken cancellationToken)
    {
        var submission = await repository.GetSubmissionAsync(tenantId, submissionId, cancellationToken);
        if (submission is null || submission.Status != ApprovalStatus.PendingApproval)
        {
            return;
        }

        submission.Decide(approved, byUserId, comment, DateTimeOffset.UtcNow);
        foreach (var entry in await repository.ListEntriesOfSubmissionAsync(tenantId, submissionId, cancellationToken))
        {
            if (entry.Status == ApprovalStatus.PendingApproval)
            {
                entry.SetDecision(approved);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
