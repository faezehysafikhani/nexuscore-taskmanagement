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
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken);
}
