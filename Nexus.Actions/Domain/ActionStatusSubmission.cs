using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Domain;

/// <summary>
/// A request to move an action to another status, waiting for someone to confirm it. Until it is approved the action stays as it is;
/// on approval it takes <see cref="SubmittedStatus"/>.
/// </summary>
public sealed class ActionStatusSubmission : AuditableEntity<Guid>
{
    private ActionStatusSubmission() : base(Guid.Empty)
    {
    }

    public ActionStatusSubmission(
        Guid id, Guid tenantId, Guid? projectId, Guid actionId, ActionStatus previousStatus, ActionStatus submittedStatus, string? comment) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        ActionId = actionId;
        PreviousStatus = previousStatus;
        SubmittedStatus = submittedStatus;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The action's project (null for a standalone action), kept so a project's requests can be listed together.</summary>
    public Guid? ProjectId { get; private set; }

    public Guid ActionId { get; private set; }
    public ActionStatus PreviousStatus { get; private set; }
    public ActionStatus SubmittedStatus { get; private set; }
    public string? Comment { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
