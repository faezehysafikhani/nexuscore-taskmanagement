using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Domain;

/// <summary>
/// A request to move a task to another column, waiting for someone to confirm it. Until it is approved the task stays where it is;
/// on approval it moves to <see cref="SubmittedStatus"/>.
/// </summary>
public sealed class AgileTaskStatusSubmission : AuditableEntity<Guid>
{
    private AgileTaskStatusSubmission() : base(Guid.Empty)
    {
    }

    public AgileTaskStatusSubmission(
        Guid id, Guid tenantId, Guid projectId, Guid taskId, AgileTaskStatus previousStatus, AgileTaskStatus submittedStatus, string? comment) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        TaskId = taskId;
        PreviousStatus = previousStatus;
        SubmittedStatus = submittedStatus;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid TaskId { get; private set; }

    /// <summary>The column the task was in when the request was made.</summary>
    public AgileTaskStatus PreviousStatus { get; private set; }

    /// <summary>The column the submitter asks to move it to.</summary>
    public AgileTaskStatus SubmittedStatus { get; private set; }

    public string? Comment { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
