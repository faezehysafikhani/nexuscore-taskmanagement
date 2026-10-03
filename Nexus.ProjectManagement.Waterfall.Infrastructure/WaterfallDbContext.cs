using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class WaterfallDbContext(DbContextOptions<WaterfallDbContext> options)
    : DbContext(options), IWaterfallUnitOfWork
{
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityDependency> ActivityDependencies => Set<ActivityDependency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaterfallDbContext).Assembly);
    }
}
