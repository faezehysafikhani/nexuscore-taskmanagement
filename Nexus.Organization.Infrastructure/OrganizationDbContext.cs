using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;

namespace Nexus.Organization.Infrastructure;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options), IOrganizationUnitOfWork
{
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
