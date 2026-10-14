using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>This module's own tables, in their own schema (org_structure). Nothing else writes them.</summary>
public sealed class OrgStructureDbContext(DbContextOptions<OrgStructureDbContext> options)
    : DbContext(options), IOrgStructureUnitOfWork
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyUnit> CompanyUnits => Set<CompanyUnit>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<UserAssignment> UserAssignments => Set<UserAssignment>();
    public DbSet<Honorific> Honorifics => Set<Honorific>();
    public DbSet<UserHonorific> UserHonorifics => Set<UserHonorific>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrgStructureDbContext).Assembly);
    }
}
