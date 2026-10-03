using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Domain;

public enum ActionStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled
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

    public void ClearRecurrence()
    {
        RecurrenceUnit = null;
        RecurrenceInterval = null;
        RecurrenceEndDate = null;
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
