using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Domain;

public enum ActionStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
    /// <summary>Added later; stored as its integer value (4), so existing rows are untouched.</summary>
    OnHold
}

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ActionPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ActionRecurrenceUnit
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}

/// <summary>
/// Named ActionItem, not Action, to avoid colliding with System.Action. Required references:
/// OrganizationUnitId and WorkCalendarId (validated to exist by ActionService against the
/// Organization/Calendar modules this project hard-references). ProjectId is optional and
/// deliberately just a Guid - ProjectManagement.Core is never referenced from this module, so
/// Actions works standalone (see rule: "این Module باید بدون Project Management نیز قابل استفاده باشد").
/// </summary>
public sealed class ActionItem : AuditableEntity<Guid>
{
    private ActionItem() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ActionItem(Guid id, Guid tenantId, string title, Guid organizationUnitId, Guid workCalendarId, Guid? projectId = null) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        OrganizationUnitId = organizationUnitId;
        WorkCalendarId = workCalendarId;
        ProjectId = projectId;
        Status = ActionStatus.Open;
        Priority = ActionPriority.Normal;
        ApprovalStatus = ApprovalStatus.NotSubmitted;

        RaiseDomainEvent(new ActionCreated(Id, TenantId, Title, ProjectId));
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public ActionStatus Status { get; private set; }
    public ActionPriority Priority { get; private set; }

    /// <summary>What the action was raised from ("Risk", "Stakeholder", ...), as an opaque label: this module knows nothing about those
    /// modules, it only remembers the link. Null = created by hand.</summary>
    public string? SourceType { get; private set; }
    public Guid? SourceId { get; private set; }

    /// <summary>The people named to approve this action, in order (up to three). Recorded with the action; which approvers a submission
    /// actually goes to is decided by the approval backend (Workflow), so with none configured these are information only.</summary>
    public Guid? Approver1UserId { get; private set; }
    public Guid? Approver2UserId { get; private set; }
    public Guid? Approver3UserId { get; private set; }

    public IReadOnlyList<Guid> ApproverUserIds =>
        new[] { Approver1UserId, Approver2UserId, Approver3UserId }.Where(id => id is not null).Select(id => id!.Value).ToList();

    public void SetApprovers(IReadOnlyList<Guid> approvers)
    {
        Approver1UserId = approvers.Count > 0 ? approvers[0] : null;
        Approver2UserId = approvers.Count > 1 ? approvers[1] : null;
        Approver3UserId = approvers.Count > 2 ? approvers[2] : null;
    }
    public Guid OrganizationUnitId { get; private set; }
    public Guid WorkCalendarId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>When set, completing the action creates the next occurrence: the same action with its
    /// dates moved on by <see cref="RecurrenceInterval"/> units. Null = a one-off action.</summary>
    public ActionRecurrenceUnit? RecurrenceUnit { get; private set; }

    /// <summary>Every how many units (every 2 weeks = Weekly, 2). At least 1 whenever a unit is set.</summary>
    public int? RecurrenceInterval { get; private set; }

    /// <summary>No occurrence is created that would start after this date. Null = no end.</summary>
    public DateOnly? RecurrenceEndDate { get; private set; }

    /// <summary>Optional: which days of the week (bit i = <see cref="DayOfWeek"/> i, Sunday = 0) a repeating action falls on.
    /// Null = the rule's default for its unit. Only read when the occurrences of the rule are listed.</summary>
    public int? RecurrenceDaysOfWeek { get; private set; }

    /// <summary>Optional: the day of the Jalali month (1-31) a monthly rule falls on.</summary>
    public int? RecurrenceDayOfMonth { get; private set; }

    /// <summary>Optional: the Jalali months (bit m-1 = month m) a monthly rule falls in. Null = every month.</summary>
    public int? RecurrenceMonths { get; private set; }

    /// <summary>Optional: how many hours each occurrence takes.</summary>
    public decimal? RecurrenceDurationHours { get; private set; }

    /// <summary>The previous occurrence this one was created from.</summary>
    public Guid? RecurrenceSourceId { get; private set; }

    /// <summary>The occurrence created when this one was completed; also what stops completing
    /// it a second time (after a reopen) from creating a duplicate.</summary>
    public Guid? NextOccurrenceId { get; private set; }

    public void UpdateDetails(
        string title, string? description, Guid? ownerUserId, Guid? responsibleUserId,
        Guid organizationUnitId, Guid workCalendarId, Guid? projectId,
        DateOnly? startDate, DateOnly? endDate)
    {
        Title = title.Trim();
        Description = description;
        OwnerUserId = ownerUserId;
        ResponsibleUserId = responsibleUserId;
        OrganizationUnitId = organizationUnitId;
        WorkCalendarId = workCalendarId;
        ProjectId = projectId;
        StartDate = startDate;
        EndDate = endDate;
    }

    public void ChangeStatus(ActionStatus status) => Status = status;

    public void SetRecurrence(ActionRecurrenceUnit unit, int interval, DateOnly? endDate)
    {
        RecurrenceUnit = unit;
        RecurrenceInterval = interval;
        RecurrenceEndDate = endDate;
    }

    public void SetRecurrencePattern(int? daysOfWeekMask, int? dayOfMonth, int? monthsMask, decimal? durationHours)
    {
        RecurrenceDaysOfWeek = daysOfWeekMask;
        RecurrenceDayOfMonth = dayOfMonth;
        RecurrenceMonths = monthsMask;
        RecurrenceDurationHours = durationHours;
    }

    public void SetSource(string? sourceType, Guid? sourceId)
    {
        SourceType = string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim();
        SourceId = SourceType is null ? null : sourceId;
    }

    public void ClearRecurrence()
    {
        RecurrenceUnit = null;
        RecurrenceInterval = null;
        RecurrenceEndDate = null;
        SetRecurrencePattern(null, null, null, null);
    }

    /// <summary>
    /// Whether completing this action should create a next occurrence, and if so the same action
    /// moved on by one interval; null when it should not (one-off, already created, no dates to move,
    /// or the next one would start after the recurrence end date). Remembers the new occurrence's id.
    /// </summary>
    public ActionItem? CreateNextOccurrence(Guid newId)
    {
        if (RecurrenceUnit is not { } unit || RecurrenceInterval is not { } interval || NextOccurrenceId is not null)
        {
            return null;
        }

        var anchor = StartDate ?? EndDate;
        if (anchor is null)
        {
            return null;
        }

        var nextStart = StartDate is null ? (DateOnly?)null : Shift(StartDate.Value, unit, interval);
        var nextEnd = EndDate is null ? (DateOnly?)null : Shift(EndDate.Value, unit, interval);
        if (RecurrenceEndDate is { } last && (nextStart ?? nextEnd)!.Value > last)
        {
            return null;
        }

        var next = new ActionItem(newId, TenantId, Title, OrganizationUnitId, WorkCalendarId, ProjectId);
        next.UpdateDetails(Title, Description, OwnerUserId, ResponsibleUserId, OrganizationUnitId, WorkCalendarId, ProjectId, nextStart, nextEnd);
        next.ChangePriority(Priority);
        next.SetRecurrence(unit, interval, RecurrenceEndDate);
        next.SetRecurrencePattern(RecurrenceDaysOfWeek, RecurrenceDayOfMonth, RecurrenceMonths, RecurrenceDurationHours);
        next.SetSource(SourceType, SourceId);
        next.SetApprovers(ApproverUserIds);
        next.RecurrenceSourceId = Id;
        NextOccurrenceId = newId;
        return next;
    }

    /// <summary>A date moved on by <paramref name="interval"/> units. Months and years keep the day of month,
    /// or the last day of a shorter month (31 Jan + 1 month = 28/29 Feb).</summary>
    public static DateOnly Shift(DateOnly date, ActionRecurrenceUnit unit, int interval) => unit switch
    {
        ActionRecurrenceUnit.Daily => date.AddDays(interval),
        ActionRecurrenceUnit.Weekly => date.AddDays(7 * interval),
        ActionRecurrenceUnit.Monthly => date.AddMonths(interval),
        ActionRecurrenceUnit.Yearly => date.AddYears(interval),
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    public void ChangePriority(ActionPriority priority) => Priority = priority;

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
