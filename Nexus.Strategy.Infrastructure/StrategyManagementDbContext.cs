using Microsoft.EntityFrameworkCore;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Domain;

namespace Nexus.StrategyManagement.Infrastructure;

public sealed class StrategyManagementDbContext(DbContextOptions<StrategyManagementDbContext> options)
    : DbContext(options), IStrategyUnitOfWork
{
    public DbSet<Strategy> Strategies => Set<Strategy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StrategyManagementDbContext).Assembly);
    }
}
