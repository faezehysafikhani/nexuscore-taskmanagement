using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IProgressSnapshotRepository
{
    Task<ProgressSnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ProgressSnapshot?> GetByDateAsync(Guid projectId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>A project's snapshots, oldest first.</summary>
    Task<IReadOnlyList<ProgressSnapshot>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task AddAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken);
    Task RemoveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken);
}
