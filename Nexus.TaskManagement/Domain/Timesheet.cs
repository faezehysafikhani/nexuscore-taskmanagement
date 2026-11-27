using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

public enum TimeEntrySource
{
    Manual = 0,
    Timer = 1
}

/// <summary>Time one person spent on one task on one day. Once submitted with its week it can no longer be changed.</summary>
public sealed class TimeEntry : AuditableEntity<Guid>
{
    private TimeEntry() : base(Guid.Empty)
    {
    }

    public TimeEntry(Guid id, Guid tenantId, Guid taskId, Guid userId, DateOnly date, int minutes, string? note, TimeEntrySource source) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        UserId = userId;
        Date = date;
        Minutes = minutes;
        Note = note;
        Source = source;
        Status = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public int Minutes { get; private set; }
    public string? Note { get; private set; }
    public TimeEntrySource Source { get; private set; }

    /// <summary>NotSubmitted (a draft), PendingApproval, Approved or Rejected - decided together with the week.</summary>
    public ApprovalStatus Status { get; private set; }
    public Guid? SubmissionId { get; private set; }

    public bool IsLocked => Status is ApprovalStatus.PendingApproval or ApprovalStatus.Approved;

    public void Update(DateOnly date, int minutes, string? note)
    {
        Date = date;
        Minutes = minutes;
        Note = note;
        // A rejected entry that is corrected goes back to being a draft.
        Status = ApprovalStatus.NotSubmitted;
        SubmissionId = null;
    }

    public void MarkSubmitted(Guid submissionId)
    {
        Status = ApprovalStatus.PendingApproval;
        SubmissionId = submissionId;
    }

    public void SetDecision(bool approved) => Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
}

/// <summary>A person's week sent for approval. WeekStart is the Saturday that opens the week.</summary>
public sealed class TimesheetSubmission : AuditableEntity<Guid>
{
    private TimesheetSubmission() : base(Guid.Empty)
    {
    }

    public TimesheetSubmission(Guid id, Guid tenantId, Guid userId, DateOnly weekStart, int totalMinutes, DateTimeOffset nowUtc) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        WeekStart = weekStart;
        TotalMinutes = totalMinutes;
        SubmittedAtUtc = nowUtc;
        Status = ApprovalStatus.PendingApproval;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public int TotalMinutes { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public ApprovalStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionComment { get; private set; }

    public void Decide(bool approved, Guid? byUserId, string? comment, DateTimeOffset nowUtc)
    {
        Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        DecidedByUserId = byUserId;
        DecidedAtUtc = nowUtc;
        DecisionComment = comment;
    }
}

/// <summary>The timer a person has running (at most one): stopping it makes a time entry.</summary>
public sealed class RunningTimer
{
    private RunningTimer()
    {
    }

    public RunningTimer(Guid userId, Guid tenantId, Guid taskId, DateTimeOffset startedAtUtc)
    {
        UserId = userId;
        TenantId = tenantId;
        TaskId = taskId;
        StartedAtUtc = startedAtUtc;
    }

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
}
