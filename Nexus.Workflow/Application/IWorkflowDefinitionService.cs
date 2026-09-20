using Nexus.Workflow.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

public interface IWorkflowDefinitionService
{
    Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListAsync(Guid tenantId, string? subjectType, CancellationToken cancellationToken);
    Task<Result<WorkflowDefinitionDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionRequest request, CancellationToken cancellationToken);
    Task<Result<WorkflowDefinitionDto>> AddStepAsync(Guid id, AddWorkflowStepRequest request, CancellationToken cancellationToken);
    Task<Result<WorkflowDefinitionDto>> DeleteStepAsync(Guid id, Guid stepId, CancellationToken cancellationToken);
    Task<Result<WorkflowDefinitionDto>> MoveStepAsync(Guid id, Guid stepId, MoveWorkflowStepRequest request, CancellationToken cancellationToken);

    /// <summary>Deactivates a scope-specific override so resolution falls back to the General definition.</summary>
    Task<Result> ResetToDefaultAsync(Guid id, CancellationToken cancellationToken);
}
