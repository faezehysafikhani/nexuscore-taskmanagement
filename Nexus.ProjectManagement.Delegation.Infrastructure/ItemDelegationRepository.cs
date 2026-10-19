using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Domain;

namespace Nexus.ProjectManagement.Delegation.Infrastructure;

public sealed class ItemDelegationRepository(ItemDelegationDbContext dbContext) : IItemDelegationRepository
{
    public Task<ItemDelegation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ItemDelegations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ItemDelegation>> ListForUserAsync(Guid tenantId, Guid userId, bool activeOnly, CancellationToken cancellationToken) =>
        await dbContext.ItemDelegations
            .Where(d => d.TenantId == tenantId && (d.DelegatorUserId == userId || d.DelegateUserId == userId) && (!activeOnly || d.IsActive))
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ItemDelegation>> ListActiveForItemAsync(Guid tenantId, DelegatedItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        await dbContext.ItemDelegations
            .Where(d => d.TenantId == tenantId && d.ItemType == itemType && d.ItemId == itemId && d.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ItemDelegation>> ListForItemAsync(Guid tenantId, DelegatedItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        await dbContext.ItemDelegations
            .Where(d => d.TenantId == tenantId && d.ItemType == itemType && d.ItemId == itemId)
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ItemDelegation>> ListForProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ItemDelegations
            .Where(d => d.TenantId == tenantId && d.ProjectId == projectId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ItemDelegation delegation, CancellationToken cancellationToken) =>
        await dbContext.ItemDelegations.AddAsync(delegation, cancellationToken);
}
