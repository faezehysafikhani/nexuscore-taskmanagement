using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;

namespace NexusCore.Infrastructure.Persistence;

/// <summary>
/// Seeds the default tenant/admin plus every permission contributed by an installed module.
/// The permission set comes from <see cref="IPermissionCatalog"/> (one per installed module),
/// so a module that was never registered contributes nothing here either.
/// </summary>
public sealed class DefaultDataSeeder(
    NexusCoreDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEnumerable<IPermissionCatalog> permissionCatalogs,
    IOptions<IdentitySeedOptions> seedOptions,
    ILogger<DefaultDataSeeder> logger)
{
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AdminUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await ModuleSchemaInitializer.EnsureCreatedAsync(dbContext, cancellationToken);

        if (!await dbContext.Tenants.AnyAsync(cancellationToken))
        {
            await dbContext.Tenants.AddAsync(new Tenant(DefaultTenantId, "Default Organization", "default"), cancellationToken);
        }

        var allPermissions = permissionCatalogs
            .SelectMany(catalog => catalog.GetPermissions())
            .DistinctBy(permission => permission.Name);

        foreach (var permission in allPermissions)
        {
            if (!await dbContext.Permissions.AnyAsync(x => x.Name == permission.Name, cancellationToken))
            {
                await dbContext.Permissions.AddAsync(new Permission(CreateStableGuid(permission.Name), permission.Name, permission.Module, permission.Description), cancellationToken);
            }
        }

        if (!await dbContext.Roles.AnyAsync(role => role.Id == AdminRoleId, cancellationToken))
        {
            await dbContext.Roles.AddAsync(new Role(AdminRoleId, DefaultTenantId, "Administrator", "Built-in full access role", isSystem: true), cancellationToken);
        }

        if (!await dbContext.Users.AnyAsync(user => user.Id == AdminUserId, cancellationToken))
        {
            var admin = new User(AdminUserId, DefaultTenantId, "admin@nexus.local", "System Administrator", passwordHasher.HashPassword("Admin@12345"), true);
            admin.AssignRole(AdminRoleId);
            await dbContext.Users.AddAsync(admin, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var adminRole = await dbContext.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role => role.Id == AdminRoleId, cancellationToken);

        var permissionIds = await dbContext.Permissions.Select(permission => permission.Id).ToListAsync(cancellationToken);
        adminRole.SetPermissions(permissionIds);

        await SeedConfiguredRolesAsync(cancellationToken);

        if (!await dbContext.Settings.AnyAsync(setting => setting.Key == "Localization.DefaultCulture", cancellationToken))
        {
            await dbContext.Settings.AddAsync(new SystemSetting(Guid.NewGuid(), null, "Localization.DefaultCulture", "fa-IR", "System"), cancellationToken);
            await dbContext.Settings.AddAsync(new SystemSetting(Guid.NewGuid(), null, "Localization.Direction", "rtl", "System"), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedConfiguredRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in seedOptions.Value.SeedRoles.Where(role => !string.IsNullOrWhiteSpace(role.Name)))
        {
            var normalized = definition.Name.Trim().ToUpperInvariant();
            if (await dbContext.Roles.AnyAsync(role => role.TenantId == DefaultTenantId && role.NormalizedName == normalized, cancellationToken))
            {
                continue; // Exists already: whatever an administrator did to it stays.
            }

            var wanted = definition.Permissions.Select(name => name.Trim()).ToHashSet(StringComparer.Ordinal);
            var permissions = await dbContext.Permissions
                .Where(permission => wanted.Contains(permission.Name))
                .Select(permission => new { permission.Id, permission.Name })
                .ToListAsync(cancellationToken);

            foreach (var missing in wanted.Except(permissions.Select(p => p.Name)))
            {
                logger.LogWarning("Seed role {Role} names unknown permission {Permission}; it was skipped.", definition.Name, missing);
            }

            var role = new Role(CreateStableGuid("role:" + normalized), DefaultTenantId, definition.Name.Trim(), definition.Description);
            role.SetPermissions(permissions.Select(p => p.Id));
            await dbContext.Roles.AddAsync(role, cancellationToken);
        }
    }

    private static Guid CreateStableGuid(string value)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }
}
