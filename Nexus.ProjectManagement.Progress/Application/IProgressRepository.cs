using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IProgressRepository
{
    Task<ProgressUpdate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProgressUpdate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The items of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<ProgressUpdate>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This repository cannot list the items of several projects.");
    Task AddAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken);
    Task RemoveAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken);
}
