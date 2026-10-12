using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IDelayReasonRepository
{
    Task<DelayReason?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DelayReason>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The items of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<DelayReason>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This repository cannot list the items of several projects.");
    Task AddAsync(DelayReason delayReason, CancellationToken cancellationToken);
    Task RemoveAsync(DelayReason delayReason, CancellationToken cancellationToken);
}
