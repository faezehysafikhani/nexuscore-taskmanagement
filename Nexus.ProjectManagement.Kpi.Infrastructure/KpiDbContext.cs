using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Infrastructure;

public sealed class KpiDbContext(DbContextOptions<KpiDbContext> options)
    : DbContext(options), IKpiUnitOfWork
{
    public DbSet<KpiDefinition> KpiDefinitions => Set<KpiDefinition>();
    public DbSet<KpiMeasurement> KpiMeasurements => Set<KpiMeasurement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KpiDbContext).Assembly);
    }
}
