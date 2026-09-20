using Nexus.StrategyManagement.Domain;

namespace Nexus.StrategyManagement.Application;

public interface IStrategyRepository
{
    Task<Strategy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Strategy>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddAsync(Strategy strategy, CancellationToken cancellationToken);
    Task RemoveAsync(Strategy strategy, CancellationToken cancellationToken);
}
