using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileDbContext(DbContextOptions<AgileDbContext> options)
    : DbContext(options), IAgileUnitOfWork
{
    public DbSet<AgileTask> AgileTasks => Set<AgileTask>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<SprintEvent> SprintEvents => Set<SprintEvent>();
    public DbSet<AgileChecklistItem> ChecklistItems => Set<AgileChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgileDbContext).Assembly);
    }
}
