using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

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

    public decimal PlannedProgress { get; private set; }
    public decimal ActualProgress { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

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
