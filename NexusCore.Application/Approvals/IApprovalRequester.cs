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
    Guid? ScopeId = null);

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
