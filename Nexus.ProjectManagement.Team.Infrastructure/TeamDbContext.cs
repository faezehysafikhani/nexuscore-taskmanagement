using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Domain;

namespace Nexus.ProjectManagement.Team.Infrastructure;

public sealed class TeamDbContext(DbContextOptions<TeamDbContext> options)
    : DbContext(options), ITeamUnitOfWork
{
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<GovernanceRole> GovernanceRoles => Set<GovernanceRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TeamDbContext).Assembly);
    }
}
