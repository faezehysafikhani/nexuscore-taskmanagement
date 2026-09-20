using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Domain;

namespace Nexus.ProjectManagement.Core.Infrastructure;

public sealed class ProjectManagementCoreDbContext(DbContextOptions<ProjectManagementCoreDbContext> options)
    : DbContext(options), IProjectManagementUnitOfWork
{
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectManagementCoreDbContext).Assembly);
    }
}
