using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Infrastructure;

public sealed class ActionsDbContext(DbContextOptions<ActionsDbContext> options)
    : DbContext(options), IActionsUnitOfWork
{
    public DbSet<ActionItem> Actions => Set<ActionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActionsDbContext).Assembly);
    }
}
