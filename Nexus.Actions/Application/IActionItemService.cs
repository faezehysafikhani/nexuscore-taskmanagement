using Nexus.Actions.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Actions.Application;

public interface IActionItemService
{
    /// <summary>projectId null lists every action regardless of project relation; pass a value
    /// to scope to one project's actions.</summary>
    Task<Result<IReadOnlyList<ActionItemDto>>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);
    Task<Result<ActionItemDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ActionItemDto>> CreateAsync(CreateActionItemRequest request, CancellationToken cancellationToken);
    Task<Result<ActionItemDto>> UpdateAsync(Guid id, UpdateActionItemRequest request, CancellationToken cancellationToken);
    Task<Result<ActionItemDto>> ChangeStatusAsync(Guid id, ChangeActionStatusRequest request, CancellationToken cancellationToken);
    Task<Result<ActionItemDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
