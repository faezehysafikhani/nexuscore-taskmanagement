using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileTaskRepository
{
    Task<AgileTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>sprintNumber null returns the whole board (all sprints); pass a value for the
    /// "Sprint View" - board scoped to one sprint.</summary>
    Task<IReadOnlyList<AgileTask>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken);

    Task AddAsync(AgileTask task, CancellationToken cancellationToken);
    Task RemoveAsync(AgileTask task, CancellationToken cancellationToken);
}
