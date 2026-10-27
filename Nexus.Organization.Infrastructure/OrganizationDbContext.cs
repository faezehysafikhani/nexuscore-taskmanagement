using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using NexusCore.Application.Security;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Organization.Infrastructure;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, ITenantScope? tenantScope = null)
    : DbContext(options), IOrganizationUnitOfWork
{
    // The organization of the signed-in user, or null when nothing confines the work (ITenantScope).
    private Guid? TenantFilterId => tenantScope?.FilterTenantIdFor(nameof(OrganizationDbContext));

    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
        modelBuilder.ApplyTenantFilters(() => TenantFilterId);
    }
}
