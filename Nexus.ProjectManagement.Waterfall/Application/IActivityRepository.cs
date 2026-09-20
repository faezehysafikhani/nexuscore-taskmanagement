using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityRepository
{
    Task<Activity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The full WBS tree for a project (flat list; hierarchy is reconstructed from ParentActivityId).</summary>
    Task<IReadOnlyList<Activity>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task AddAsync(Activity activity, CancellationToken cancellationToken);
    Task RemoveAsync(Activity activity, CancellationToken cancellationToken);
}
