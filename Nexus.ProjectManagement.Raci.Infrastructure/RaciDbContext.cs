using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Raci.Application;
using Nexus.ProjectManagement.Raci.Domain;

namespace Nexus.ProjectManagement.Raci.Infrastructure;

public sealed class RaciDbContext(DbContextOptions<RaciDbContext> options)
    : DbContext(options), IRaciUnitOfWork
{
    public DbSet<RaciAssignment> RaciAssignments => Set<RaciAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaciDbContext).Assembly);
    }
}
