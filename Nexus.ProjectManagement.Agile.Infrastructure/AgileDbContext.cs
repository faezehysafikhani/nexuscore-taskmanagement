using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileDbContext(DbContextOptions<AgileDbContext> options)
    : DbContext(options), IAgileUnitOfWork
{
    public DbSet<AgileTask> AgileTasks => Set<AgileTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgileDbContext).Assembly);
    }
}
