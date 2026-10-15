using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Domain;

namespace Nexus.ProjectManagement.Delegation.Infrastructure;

public sealed class ItemDelegationDbContext(DbContextOptions<ItemDelegationDbContext> options)
    : DbContext(options), IItemDelegationUnitOfWork
{
    public DbSet<ItemDelegation> ItemDelegations => Set<ItemDelegation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ItemDelegationDbContext).Assembly);
    }
}
