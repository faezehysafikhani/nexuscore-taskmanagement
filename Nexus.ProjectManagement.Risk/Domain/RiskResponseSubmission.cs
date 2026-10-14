using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Domain;

/// <summary>
/// A report of where the work on a risk's response stands, waiting for someone to verify it. Until it is approved the risk keeps its
/// response status; on approval it takes <see cref="SubmittedStatus"/>.
/// </summary>
public sealed class RiskResponseSubmission : AuditableEntity<Guid>
{
    private RiskResponseSubmission() : base(Guid.Empty)
    {
    }

    public RiskResponseSubmission(
        Guid id, Guid tenantId, Guid projectId, Guid riskId, RiskResponseStatus previousStatus, RiskResponseStatus submittedStatus, string? comment) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        RiskId = riskId;
        PreviousStatus = previousStatus;
        SubmittedStatus = submittedStatus;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid RiskId { get; private set; }
    public RiskResponseStatus PreviousStatus { get; private set; }
    public RiskResponseStatus SubmittedStatus { get; private set; }
    public string? Comment { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
