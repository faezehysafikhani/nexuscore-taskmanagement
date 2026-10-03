using Nexus.Workflow.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

public interface IWorkflowDelegationService
{
    /// <summary>Delegations the user has given and received.</summary>
    Task<Result<IReadOnlyList<WorkflowDelegationDto>>> ListMineAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<Result<WorkflowDelegationDto>> CreateAsync(
        Guid tenantId, Guid currentUserId, bool canManageOthers, CreateWorkflowDelegationRequest request, CancellationToken cancellationToken);

    /// <summary>Ends a delegation now. Only the delegator (or someone who may manage others') can.</summary>
    Task<Result<WorkflowDelegationDto>> RevokeAsync(Guid tenantId, Guid id, Guid currentUserId, bool canManageOthers, CancellationToken cancellationToken);
}
