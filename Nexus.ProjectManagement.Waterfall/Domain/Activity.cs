using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>Low / medium / high, for an activity's importance and complexity. Stored as its integer value; never renumber.</summary>
public enum ActivityLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// WBS node. ProjectId is a required reference to ProjectManagement.Core's Project (Waterfall
/// depends on Core); DeliverableId is a soft, optional reference to the Deliverables capability
/// (which may not be installed) - a bare Guid, never a navigation property.
/// </summary>
public sealed class Activity : AuditableEntity<Guid>
{
    private Activity() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public Activity(Guid id, Guid tenantId, Guid projectId, string name, Guid? parentActivityId = null) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Name = name.Trim();
        ParentActivityId = parentActivityId;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? ParentActivityId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? DeliverableId { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public Guid? ApproverUserId { get; private set; }

    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public int? DurationDays { get; private set; }
    public decimal? ManHours { get; private set; }
    public decimal Weight { get; private set; }

    /// <summary>A milestone is a zero-duration marker (a decision, a hand-over): it takes no time
    /// of its own, so DurationDays is 0 and its end date is its start date. It is always a leaf.</summary>
    public bool IsMilestone { get; private set; }

    /// <summary>Where the activity sits among the project's activities (siblings are listed in this order). Set when it is created;
    /// activities that predate it all read 0 and fall back to creation order.</summary>
    public int Position { get; private set; }

    public void SetPosition(int position) => Position = position;

    /// <summary>What the activity is expected to cost; one input of cost-based and combined weighting. Null = not estimated.</summary>
    public decimal? Cost { get; private set; }

    /// <summary>How much the activity matters to the project's outcome.</summary>
    public ActivityLevel Importance { get; private set; } = ActivityLevel.Medium;

    /// <summary>How hard the activity is; one input of complexity-based and combined weighting.</summary>
    public ActivityLevel Complexity { get; private set; } = ActivityLevel.Medium;

    public decimal PlannedProgress { get; private set; }
    public decimal ActualProgress { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>A value left null is left as it is, so a caller that predates these estimates cannot wipe them.</summary>
    public void SetEstimates(decimal? cost, ActivityLevel? importance, ActivityLevel? complexity)
    {
        if (cost is not null)
        {
            Cost = Math.Max(cost.Value, 0);
        }

        if (importance is not null)
        {
            Importance = importance.Value;
        }

        if (complexity is not null)
        {
            Complexity = complexity.Value;
        }
    }

    public void UpdateDetails(
        string name, string? description, Guid? parentActivityId, Guid? deliverableId,
        Guid? responsibleUserId, Guid? approverUserId,
        DateOnly? startDate, DateOnly? endDate, int? durationDays, decimal? manHours, decimal weight)
    {
        Name = name.Trim();
        Description = description;
        ParentActivityId = parentActivityId;
        DeliverableId = deliverableId;
        ResponsibleUserId = responsibleUserId;
        ApproverUserId = approverUserId;
        StartDate = startDate;
        EndDate = endDate;
        DurationDays = durationDays;
        ManHours = manHours;
        Weight = weight;
    }

    /// <summary>Call after <see cref="UpdateDetails"/>: turning a milestone on zeroes its duration
    /// and puts its end on its start, so the two can never disagree.</summary>
    public void SetMilestone(bool isMilestone)
    {
        IsMilestone = isMilestone;
        if (!isMilestone)
        {
            return;
        }

        DurationDays = 0;
        var date = StartDate ?? EndDate;
        StartDate = date;
        EndDate = date;
    }

    /// <summary>Writes the dates a schedule calculation produced; the activity's own other
    /// details are untouched.</summary>
    public void ApplySchedule(DateOnly startDate, DateOnly endDate, int durationDays)
    {
        StartDate = startDate;
        EndDate = endDate;
        DurationDays = durationDays;
    }

    public void UpdateProgress(decimal plannedProgress, decimal actualProgress)
    {
        PlannedProgress = Math.Clamp(plannedProgress, 0, 100);
        ActualProgress = Math.Clamp(actualProgress, 0, 100);
        RaiseDomainEvent(new ActivityProgressUpdated(Id, TenantId, ProjectId, PlannedProgress, ActualProgress));
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
