using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Security;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>This module's own tables, in their own schema (org_structure). Nothing else writes them.</summary>
public sealed class OrgStructureDbContext(DbContextOptions<OrgStructureDbContext> options, ITenantScope? tenantScope = null)
    : DbContext(options), IOrgStructureUnitOfWork
{
    // The organization of the signed-in user, or null when nothing confines the work (ITenantScope).
    private Guid? TenantFilterId => tenantScope?.FilterTenantIdFor(nameof(OrgStructureDbContext));

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyUnit> CompanyUnits => Set<CompanyUnit>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<UserAssignment> UserAssignments => Set<UserAssignment>();
    public DbSet<PositionCompany> PositionCompanies => Set<PositionCompany>();
    public DbSet<TaskPost> TaskPosts => Set<TaskPost>();
    public DbSet<TaskPostPeriod> TaskPostPeriods => Set<TaskPostPeriod>();
    public DbSet<PostActing> PostActings => Set<PostActing>();
    public DbSet<UnitPosition> UnitPositions => Set<UnitPosition>();
    public DbSet<UserPosition> UserPositions => Set<UserPosition>();
    public DbSet<Honorific> Honorifics => Set<Honorific>();
    public DbSet<UserHonorific> UserHonorifics => Set<UserHonorific>();
    public DbSet<NodeType> NodeTypes => Set<NodeType>();
    public DbSet<NodeTypeRule> NodeTypeRules => Set<NodeTypeRule>();
    public DbSet<NodeTypePostTemplate> NodeTypePostTemplates => Set<NodeTypePostTemplate>();
    public DbSet<LinkType> LinkTypes => Set<LinkType>();
    public DbSet<AutoLinkRule> AutoLinkRules => Set<AutoLinkRule>();
    public DbSet<OrgLink> OrgLinks => Set<OrgLink>();
    public DbSet<StructureTemplate> StructureTemplates => Set<StructureTemplate>();
    public DbSet<StructureTemplateItem> StructureTemplateItems => Set<StructureTemplateItem>();
    public DbSet<OrgChangeLog> OrgChangeLogs => Set<OrgChangeLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrgStructureDbContext).Assembly);
        modelBuilder.ApplyTenantFilters(() => TenantFilterId);
    }
}
