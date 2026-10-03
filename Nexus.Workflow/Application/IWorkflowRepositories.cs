using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Application;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Prefers an active scope-specific definition; falls back to the active General
    /// definition for the same SubjectType when no scope match exists.</summary>
    Task<WorkflowDefinition?> FindApplicableAsync(Guid tenantId, string subjectType, string? scopeType, Guid? scopeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(Guid tenantId, string? subjectType, CancellationToken cancellationToken);
    Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken);
}

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<WorkflowInstance?> GetActiveForSubjectAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowInstance>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken);

    /// <summary>As above, plus the pending approvals of the people the user is currently standing in for: a step
    /// designated to a user in <paramref name="actingFor"/> also matches (limited to the delegation's subject type when it has one).</summary>
    Task<IReadOnlyList<WorkflowInstance>> ListPendingForApproverAsync(
        Guid tenantId, Guid approverUserId, IReadOnlyCollection<ActingFor> actingFor, CancellationToken cancellationToken);
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken);
}

/// <summary>Who a user is standing in for, and for which kind of approval (null = all).</summary>
public sealed record ActingFor(Guid DelegatorUserId, string? SubjectType);

public interface IWorkflowDelegationRepository
{
    Task<WorkflowDelegation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every delegation the user is in, as delegator or as delegate, newest first.</summary>
    Task<IReadOnlyList<WorkflowDelegation>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Delegations to this user that are in force on the date.</summary>
    Task<IReadOnlyList<WorkflowDelegation>> ListActiveForDelegateAsync(Guid tenantId, Guid delegateUserId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Not-revoked delegations from this delegator to this delegate, any dates.</summary>
    Task<IReadOnlyList<WorkflowDelegation>> ListLiveBetweenAsync(Guid tenantId, Guid delegatorUserId, Guid delegateUserId, CancellationToken cancellationToken);

    Task AddAsync(WorkflowDelegation delegation, CancellationToken cancellationToken);
}
