using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

public sealed class StakeholderManagementDbContext(DbContextOptions<StakeholderManagementDbContext> options)
    : DbContext(options), IStakeholderUnitOfWork
{
    public DbSet<Stakeholder> Stakeholders => Set<Stakeholder>();
    public DbSet<StakeholderProjectSettings> ProjectSettings => Set<StakeholderProjectSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StakeholderManagementDbContext).Assembly);
    }
}
