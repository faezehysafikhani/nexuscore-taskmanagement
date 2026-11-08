using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Domain;

namespace Nexus.ProjectManagement.History.Infrastructure;

/// <summary>Deliberately registered WITHOUT AuditingInterceptor: that interceptor is what feeds this module, so
/// writing history through a context that has it would record the history of writing history.</summary>
public sealed class ProjectHistoryDbContext(DbContextOptions<ProjectHistoryDbContext> options)
    : DbContext(options), IProjectHistoryUnitOfWork
{
    public DbSet<ProjectChange> ProjectChanges => Set<ProjectChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectHistoryDbContext).Assembly);
    }
}
