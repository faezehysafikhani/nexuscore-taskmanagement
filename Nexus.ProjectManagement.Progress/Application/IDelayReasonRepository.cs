using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IDelayReasonRepository
{
    Task<DelayReason?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DelayReason>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(DelayReason delayReason, CancellationToken cancellationToken);
    Task RemoveAsync(DelayReason delayReason, CancellationToken cancellationToken);
}
