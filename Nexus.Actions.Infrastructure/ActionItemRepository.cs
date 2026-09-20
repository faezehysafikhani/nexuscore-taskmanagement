using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Infrastructure;

public sealed class ActionItemRepository(ActionsDbContext dbContext) : IActionItemRepository
{
    public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Actions.SingleOrDefaultAsync(action => action.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken)
    {
        var query = dbContext.Actions.Where(action => action.TenantId == tenantId);

        if (projectId is { } id)
        {
            query = query.Where(action => action.ProjectId == id);
        }

        return await query.OrderByDescending(action => action.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActionItem action, CancellationToken cancellationToken)
    {
        await dbContext.Actions.AddAsync(action, cancellationToken);
    }
}
