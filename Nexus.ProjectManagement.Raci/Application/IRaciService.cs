using Nexus.ProjectManagement.Raci.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Raci.Application;

public interface IRaciService
{
    Task<Result<IReadOnlyList<RaciAssignmentDto>>> ListByProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RaciAssignmentDto>>> SetItemAsync(Guid tenantId, SetItemRaciRequest request, CancellationToken cancellationToken);
}
