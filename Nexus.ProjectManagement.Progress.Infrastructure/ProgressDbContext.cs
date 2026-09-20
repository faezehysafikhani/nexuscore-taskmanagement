using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public sealed class ProgressDbContext(DbContextOptions<ProgressDbContext> options)
    : DbContext(options), IProgressUnitOfWork
{
    public DbSet<ProgressUpdate> ProgressUpdates => Set<ProgressUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProgressDbContext).Assembly);
    }
}
