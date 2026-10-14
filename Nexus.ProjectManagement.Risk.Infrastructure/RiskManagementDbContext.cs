using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskManagementDbContext(DbContextOptions<RiskManagementDbContext> options)
    : DbContext(options), IRiskUnitOfWork
{
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<RiskProjectSettings> ProjectSettings => Set<RiskProjectSettings>();
    public DbSet<RiskResponseSubmission> ResponseSubmissions => Set<RiskResponseSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RiskManagementDbContext).Assembly);
    }
}
