using Microsoft.EntityFrameworkCore;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Infrastructure.Persistence;

public sealed class NexusCoreDbContext(
    DbContextOptions<NexusCoreDbContext> options)
    : DbContext(options), IUnitOfWork
{
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
    }
}
