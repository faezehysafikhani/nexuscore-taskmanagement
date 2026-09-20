using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IProgressRepository
{
    Task<ProgressUpdate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProgressUpdate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken);
}
