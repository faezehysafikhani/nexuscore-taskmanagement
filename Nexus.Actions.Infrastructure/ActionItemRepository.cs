using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Infrastructure;

public sealed class ActionItemRepository(ActionsDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IActionItemRepository
{
    public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.Actions.SingleOrDefaultAsync(action => action.Id == id && (tenant == null || action.TenantId == tenant), cancellationToken);
    }

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

    public Task RemoveAsync(ActionItem action, CancellationToken cancellationToken)
    {
        dbContext.Actions.Remove(action);
        return Task.CompletedTask;
    }
}
