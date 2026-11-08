namespace NexusCore.Application.Approvals;

/// <summary>
/// Identifies the thing being submitted for approval, so a single generic approval backend
/// (the Workflow module, when installed) can route it to the right WorkflowDefinition without
/// the submitting module (Risk, Project, Waterfall Activity, ...) knowing anything about
/// Workflow. ScopeType/ScopeId let a scope (e.g. a specific Project) carry its own definition
/// instead of the general one - see Nexus.Integrations.ProjectWorkflow.
/// </summary>
public sealed record ApprovalSubject(
    string SubjectType,
    Guid SubjectId,
    Guid TenantId,
    string? ScopeType = null,
    Guid? ScopeId = null,
    IReadOnlyDictionary<string, Guid>? Participants = null);

/// <summary>
/// The well-known names a submitting module (or an <see cref="IApprovalParticipantResolver"/>) uses for the
/// people around a subject, so a workflow step can say "whoever is the Manager of this" instead of naming a
/// user. They are opaque keys to the approval backend; only the submitter gives them meaning.
/// </summary>
public static class ApprovalParticipants
{
    public const string Manager = "Manager";
    public const string Owner = "Owner";
    public const string Approver1 = "Approver1";
    public const string Approver2 = "Approver2";
    public const string Approver3 = "Approver3";

    public static IReadOnlyList<string> All { get; } = [Manager, Owner, Approver1, Approver2, Approver3];

    /// <summary>The participant to use when the named one is not known (an unknown Owner is answered by the Manager).</summary>
    public static string? FallbackOf(string key) => key == Owner ? Manager : null;
}

/// <summary>
/// Optional extension point: fills in the participants of a subject from what its scope knows (for example
/// a Project's manager and owner for anything scoped to that project). Participants the submitter supplied
/// itself are used only where a resolver has no answer. With none registered, only what the submitter
/// supplied exists - nothing depends on a resolver being installed.
/// </summary>
public interface IApprovalParticipantResolver
{
    Task<IReadOnlyDictionary<string, Guid>> ResolveAsync(ApprovalSubject subject, CancellationToken cancellationToken);
}

public enum ApprovalRequestOutcome
{
    /// <summary>No approval backend is installed; the caller applies its own direct-approve business rule.</summary>
    NotConfigured,

    /// <summary>An approval process was started; the caller should move the subject to "pending approval".</summary>
    Submitted
}

/// <summary>
/// Optional integration point. When no Workflow-aware module is installed, NullApprovalRequester
/// is registered and every submission is reported as NotConfigured, so callers fall back to
/// their own direct-approve rule - Workflow is never a hard requirement for any module.
/// </summary>
public interface IApprovalRequester
{
    Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken);
}
