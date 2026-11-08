using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityDependencyRepository
{
    Task<ActivityDependency?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityDependency>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Every link that has this activity at either end.</summary>
    Task<IReadOnlyList<ActivityDependency>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken);

    Task AddAsync(ActivityDependency dependency, CancellationToken cancellationToken);
    Task RemoveAsync(ActivityDependency dependency, CancellationToken cancellationToken);
    Task RemoveRangeAsync(IReadOnlyCollection<ActivityDependency> dependencies, CancellationToken cancellationToken);
}
