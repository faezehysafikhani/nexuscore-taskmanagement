using Microsoft.EntityFrameworkCore;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Infrastructure;

public sealed class AlignmentRepository(StrategyAlignmentDbContext dbContext) : IAlignmentRepository
{
    public Task<ProjectStrategyAlignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectStrategyAlignments.SingleOrDefaultAsync(alignment => alignment.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProjectStrategyAlignment>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken)
    {
        var query = dbContext.ProjectStrategyAlignments.Where(alignment => alignment.TenantId == tenantId);

        if (projectId is { } project)
        {
            query = query.Where(alignment => alignment.ProjectId == project);
        }

        if (strategyId is { } strategy)
        {
            query = query.Where(alignment => alignment.StrategyId == strategy);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ProjectStrategyAlignment alignment, CancellationToken cancellationToken)
    {
        await dbContext.ProjectStrategyAlignments.AddAsync(alignment, cancellationToken);
    }
}
