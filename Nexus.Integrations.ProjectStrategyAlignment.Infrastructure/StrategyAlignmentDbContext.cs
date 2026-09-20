using Microsoft.EntityFrameworkCore;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Infrastructure;

public sealed class StrategyAlignmentDbContext(DbContextOptions<StrategyAlignmentDbContext> options)
    : DbContext(options), IStrategyAlignmentUnitOfWork
{
    public DbSet<ProjectStrategyAlignment> ProjectStrategyAlignments => Set<ProjectStrategyAlignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StrategyAlignmentDbContext).Assembly);
    }
}
