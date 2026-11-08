using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>
/// A report that an activity has reached some percent complete, waiting for someone to confirm it. Until it is approved the
/// activity keeps the progress it had; on approval the activity takes the confirmed figure. The approver may confirm a different
/// figure than the one reported (<see cref="ApprovedProgress"/>); a rejection that still carries a confirmed figure is a
/// "partial rejection": the report is refused but the confirmed figure becomes the activity's progress.
/// </summary>
public sealed class ActivityProgressSubmission : AuditableEntity<Guid>
{
    private ActivityProgressSubmission() : base(Guid.Empty)
    {
    }

    public ActivityProgressSubmission(
        Guid id, Guid tenantId, Guid projectId, Guid activityId, decimal submittedProgress, string? comment, string? documentName) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        ActivityId = activityId;
        SubmittedProgress = Math.Clamp(submittedProgress, 0, 100);
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        DocumentName = string.IsNullOrWhiteSpace(documentName) ? null : documentName.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ActivityId { get; private set; }

    /// <summary>The percent complete the submitter reported.</summary>
    public decimal SubmittedProgress { get; private set; }

    /// <summary>The percent complete an approver confirmed, once one has (null = not decided differently).</summary>
    public decimal? ApprovedProgress { get; private set; }

    /// <summary>True when the report was rejected but a confirmed figure was applied anyway.</summary>
    public bool IsPartialRejection { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>The name of the document the submitter says supports the report (the file itself lives in Documents).</summary>
    public string? DocumentName { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>The figure the activity takes if this is approved: the confirmed one, else the reported one.</summary>
    public decimal EffectiveProgress => ApprovedProgress ?? SubmittedProgress;

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void SetApprovedProgress(decimal progress) => ApprovedProgress = Math.Clamp(progress, 0, 100);

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject()
    {
        ApprovalStatus = ApprovalStatus.Rejected;
        IsPartialRejection = ApprovedProgress is not null;
    }
}
