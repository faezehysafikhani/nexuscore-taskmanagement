using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Progress.Domain;

public enum PerformanceClassification { OnTrack, AtRisk, Behind }

public sealed class ProgressUpdate : AuditableEntity<Guid>
{
    private ProgressUpdate() : base(Guid.Empty)
    {
    }

    public ProgressUpdate(Guid id, Guid tenantId, Guid projectId, DateOnly registerDate, decimal plannedProgress, decimal actualProgress) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        RegisterDate = registerDate;
        PlannedProgress = plannedProgress;
        ActualProgress = actualProgress;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string? StatusDescription { get; private set; }
    public DateOnly RegisterDate { get; private set; }
    public decimal PlannedProgress { get; private set; }
    public decimal ActualProgress { get; private set; }

    /// <summary>Set only when this update is approved; a rejected update never changes it -
    /// the project's confirmed progress stays whatever it last was.</summary>
    public decimal? ConfirmedProgress { get; private set; }
    public string? DelayReasons { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public decimal Deviation => ActualProgress - PlannedProgress;

    public PerformanceClassification PerformanceClassification => Deviation switch
    {
        >= -5 => PerformanceClassification.OnTrack,
        >= -15 => PerformanceClassification.AtRisk,
        _ => PerformanceClassification.Behind
    };

    public void UpdateDetails(string? statusDescription, decimal plannedProgress, decimal actualProgress, string? delayReasons)
    {
        StatusDescription = statusDescription;
        PlannedProgress = plannedProgress;
        ActualProgress = actualProgress;
        DelayReasons = delayReasons;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve()
    {
        ApprovalStatus = ApprovalStatus.Approved;
        ConfirmedProgress = ActualProgress;
    }

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
