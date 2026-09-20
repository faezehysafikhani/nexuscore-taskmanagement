using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Infrastructure;

public sealed class ProjectDocumentsDbContext(DbContextOptions<ProjectDocumentsDbContext> options)
    : DbContext(options), IDocumentsUnitOfWork
{
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectDocumentsDbContext).Assembly);
    }
}
