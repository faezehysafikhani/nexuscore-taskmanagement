using Nexus.ProjectManagement.Deliverables.Domain;

namespace Nexus.ProjectManagement.Deliverables.Application;

public interface IDeliverableRepository
{
    Task<Deliverable?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Deliverable>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(Deliverable deliverable, CancellationToken cancellationToken);
}
