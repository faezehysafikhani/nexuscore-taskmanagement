using Nexus.ProjectManagement.Raci.Domain;

namespace Nexus.ProjectManagement.Raci.Application;

public interface IRaciRepository
{
    Task<IReadOnlyList<RaciAssignment>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RaciAssignment>> ListByItemAsync(Guid projectId, RaciItemType itemType, Guid itemId, CancellationToken cancellationToken);
    Task AddAsync(RaciAssignment assignment, CancellationToken cancellationToken);
    Task RemoveAsync(RaciAssignment assignment, CancellationToken cancellationToken);
}
