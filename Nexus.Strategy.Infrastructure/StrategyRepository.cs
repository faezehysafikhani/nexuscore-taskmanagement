using Microsoft.EntityFrameworkCore;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Domain;

namespace Nexus.StrategyManagement.Infrastructure;

public sealed class StrategyRepository(StrategyManagementDbContext dbContext) : IStrategyRepository
{
    public Task<Strategy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Strategies.SingleOrDefaultAsync(strategy => strategy.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Strategy>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Strategies.Where(strategy => strategy.TenantId == tenantId).OrderBy(strategy => strategy.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Strategy strategy, CancellationToken cancellationToken)
    {
        await dbContext.Strategies.AddAsync(strategy, cancellationToken);
    }

    public Task RemoveAsync(Strategy strategy, CancellationToken cancellationToken)
    {
        dbContext.Strategies.Remove(strategy);
        return Task.CompletedTask;
    }
}
