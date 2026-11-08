using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>A permission a module adds reaches the Administrator role at start-up even with the seed off.</summary>
public sealed class PermissionCatalogSyncTests
{
    private sealed class Catalog(params PermissionDefinition[] permissions) : IPermissionCatalog
    {
        public IReadOnlyList<PermissionDefinition> GetPermissions() => permissions;
    }

    private static ServiceProvider Build(string database, params PermissionDefinition[] catalog) =>
        new ServiceCollection()
            .AddDbContext<NexusCoreDbContext>(options => options.UseInMemoryDatabase(database))
            .AddSingleton<IPermissionCatalog>(new Catalog(catalog))
            .BuildServiceProvider();

    private static PermissionCatalogSync Sync(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PermissionCatalogSync>.Instance);

    [Fact]
    public async Task ANewCataloguePermission_IsAdded_AndGivenToTheAdministrator()
    {
        var old = new PermissionDefinition("users.view", "Identity", "مشاهده کاربران");
        var added = new PermissionDefinition("org_structure.manage_position_permissions", "Organization", "تعیین دسترسی‌های هر سمت");
        using var provider = Build("sync-" + Guid.NewGuid(), old, added);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var existing = new Permission(Guid.NewGuid(), old.Name, old.Module, old.Description);
            var role = new Role(DefaultDataSeeder.AdminRoleId, Guid.NewGuid(), "Administrator", isSystem: true);
            db.Permissions.Add(existing);
            db.Roles.Add(role);
            role.SetPermissions([existing.Id]);
            await db.SaveChangesAsync();
        }

        await Sync(provider).SyncAsync(default);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var names = await db.Roles.Where(r => r.Id == DefaultDataSeeder.AdminRoleId)
                .SelectMany(r => r.Permissions).Select(p => p.Permission!.Name).ToListAsync();
            Assert.Equal(new[] { added.Name, old.Name }.Order(), names.Order());
            Assert.Equal(2, await db.Permissions.CountAsync());
        }

        // Again: nothing more is added.
        await Sync(provider).SyncAsync(default);
        using (var scope = provider.CreateScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Permissions.CountAsync());
        }
    }

    [Fact]
    public async Task WithoutTheBuiltInAdministratorRole_NothingIsCreated()
    {
        using var provider = Build("sync-" + Guid.NewGuid(), new PermissionDefinition("users.view", "Identity", "مشاهده کاربران"));

        await Sync(provider).SyncAsync(default);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        Assert.Empty(await db.Permissions.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
    }
}
