using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Deliverables.Domain;

namespace Nexus.ProjectManagement.Deliverables.Infrastructure;

public sealed class DeliverablesDbContext(DbContextOptions<DeliverablesDbContext> options)
    : DbContext(options), IDeliverablesUnitOfWork
{
    public DbSet<Deliverable> Deliverables => Set<Deliverable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliverablesDbContext).Assembly);
    }
}
