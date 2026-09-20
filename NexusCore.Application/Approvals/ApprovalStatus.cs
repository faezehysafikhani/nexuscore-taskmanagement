namespace NexusCore.Application.Approvals;

/// <summary>
/// Shared vocabulary for every capability that supports optional approval (Project, Risk,
/// Stakeholder, Progress updates, Documents, ...), so they don't each redefine the same enum.
/// </summary>
public enum ApprovalStatus
{
    NotSubmitted,
    PendingApproval,
    Approved,
    Rejected
}
