using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Security;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Infrastructure.Persistence;

public sealed class NexusCoreDbContext(
    DbContextOptions<NexusCoreDbContext> options,
    ITenantScope? tenantScope = null)
    : DbContext(options), IUnitOfWork
{
    // Read per query from this context instance (EF parameterises it): the organization of the
    // signed-in user, or null when nothing confines the work (see ITenantScope).
    private Guid? TenantFilterId => tenantScope?.FilterTenantIdFor(nameof(NexusCoreDbContext));

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(NexusCoreDbContext).Assembly);

        // Users, roles and groups of other organizations are not returned. Not filtered: the audit
        // log (it holds sign-in attempts that belong to no organization, and the viewers decide
        // which tenant to read), and settings without a tenant are the system-wide ones.
        modelBuilder.ApplyTenantFilters(
            () => TenantFilterId,
            except: [typeof(AuditLog)],
            alsoNullTenant: [typeof(SystemSetting)]);
    }
}
