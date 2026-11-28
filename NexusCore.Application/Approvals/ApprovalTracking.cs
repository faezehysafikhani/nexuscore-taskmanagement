namespace NexusCore.Application.Approvals;

/// <summary>
/// Thrown by an approval backend when it will not start an approval as asked, for a reason the submitter must fix
/// (a mandatory step whose approver cannot be found, a submitter who would approve their own request with nobody
/// to hand it to). Code is stable for the client; Message is Persian and is shown as is. The platform answers it
/// with 400 and this code (see SafeErrorResponses), so a submitting module needs no handling of its own.
/// </summary>
public sealed class ApprovalRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Where the latest approval of a subject stands, as the approval backend holds it.</summary>
public enum ApprovalRunState
{
    InProgress,
    Approved,
    Rejected,
    Withdrawn
}

public sealed record ApprovalRunSnapshot(ApprovalRunState State, string? LastComment, DateTimeOffset? DecidedAtUtc);

/// <summary>
/// Optional: what the approval backend knows about a subject's approval, and taking back one it started. A module
/// uses it to repair its own copy of the approval state when the two drifted apart (its save failed after the
/// approval was started, or its handler failed after a decision was committed). With no backend installed nothing
/// is registered and callers carry on as before.
/// </summary>
public interface IApprovalTracker
{
    /// <summary>The newest approval of the subject (null when it has none).</summary>
    Task<ApprovalRunSnapshot?> GetLatestAsync(ApprovalSubject subject, CancellationToken cancellationToken);

    /// <summary>Closes any approval of the subject still in progress, so it leaves the approvers' inboxes. Returns how many.</summary>
    Task<int> WithdrawAsync(ApprovalSubject subject, CancellationToken cancellationToken);
}

/// <summary>
/// Optional: a short Persian name of a subject for messages to people ("منشور پروژه «...»"). A module registers one
/// for the subject types it owns; null when it does not know the subject.
/// </summary>
public interface IApprovalSubjectDescriber
{
    Task<string?> DescribeAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken);
}
