using Nexus.ProjectManagement.Deliverables.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Deliverables.Application;

public interface IDeliverableService
{
    Task<Result<IReadOnlyList<DeliverableDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<DeliverableDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<DeliverableDto>> CreateAsync(CreateDeliverableRequest request, CancellationToken cancellationToken);
    Task<Result<DeliverableDto>> UpdateAsync(Guid id, UpdateDeliverableRequest request, CancellationToken cancellationToken);
    Task<Result<DeliverableDto>> ChangeStatusAsync(Guid id, ChangeDeliverableStatusRequest request, CancellationToken cancellationToken);
}
