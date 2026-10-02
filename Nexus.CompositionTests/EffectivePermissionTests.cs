using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Persistence;
using Ticketing.Application.Common.Security;

namespace Nexus.CompositionTests;

/// <summary>
/// What an administrator sets on the user access page is what the server enforces: permissions
/// from roles and groups, explicit denials that override them, the product's own permission
/// scope - through the real HTTP endpoints, on the caller's current token (no new sign-in).
/// </summary>
public sealed class EffectivePermissionTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] ProductModules = ["Identity", "Platform", "TaskManagement"];

    // ---------------------------------------------------------------- the product's permissions

    [Fact]
    public async Task OnlyThisProductsPermissions_AreListed_WithTheirPersianTitles()
    {
        var groups = await JsonAsync(await host.SendAsync(host.Admin, HttpMethod.Get, "/api/identity/permissions"));
        var modules = groups.EnumerateArray().Select(g => g.GetProperty("module").GetString()).ToList();

        Assert.Contains("TaskManagement", modules);
        Assert.Contains("Identity", modules);
        Assert.All(modules, module => Assert.Contains(module, ProductModules));
        Assert.DoesNotContain("Ticketing", modules);
        Assert.All(groups.EnumerateArray().SelectMany(g => g.GetProperty("permissions").EnumerateArray()),
            permission => Assert.Matches("[؀-ۿ]", permission.GetProperty("description").GetString()!));

        var member = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        var access = await AccessAsync(member);
        Assert.All(access, entry => Assert.Contains(entry.GetProperty("module").GetString(), ProductModules));
        Assert.DoesNotContain(access, entry => entry.GetProperty("name").GetString() == TicketingPermissions.View);
    }

    [Fact]
    public async Task AnotherModulesPermission_CannotBeAssigned_ButStaysWhereItIs_AndStillWorks()
    {
        var ticketing = await host.PermissionIdsAsync(TicketingPermissions.View, TicketingPermissions.Create);
        var target = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View, TicketingPermissions.View, TicketingPermissions.Create]);

        // Not through the user, a role or a group of this product.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{target.Id}/permissions",
            new { permissionIds = ticketing })).StatusCode);
        var role = await host.RoleAsync(host.TenantA);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/roles/{role}/permissions",
            new { permissionIds = ticketing })).StatusCode);
        var group = await GroupAsync(host.TenantA, [], []);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/groups/{group}/permissions",
            new { permissionIds = ticketing })).StatusCode);

        // Saving the user's product permissions leaves the Ticketing ones they already had.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{target.Id}/permissions",
            new { permissionIds = await host.PermissionIdsAsync(TaskManagementPermissions.View, TaskManagementPermissions.Create) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(target, HttpMethod.Post, "/api/tickets", new { title = "Printer", description = "Broken", priority = "Medium" })).StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var names = await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Set<UserPermission>()
                .Where(p => p.UserId == target.Id).Select(p => p.Permission!.Name).ToListAsync();
            Assert.Contains(TicketingPermissions.View, names);
            Assert.Contains(TaskManagementPermissions.Create, names);
        }

        // The same for a role: its Ticketing permission survives a save of its task permissions.
        var ticketRole = await RoleWithAsync(host.TenantA, TicketingPermissions.View, TaskManagementPermissions.View);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/roles/{ticketRole}/permissions",
            new { permissionIds = await host.PermissionIdsAsync(TaskManagementPermissions.Create) })).StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var names = await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Roles
                .Where(r => r.Id == ticketRole).SelectMany(r => r.Permissions).Select(p => p.Permission!.Name).ToListAsync();
            Assert.Equal(new[] { TaskManagementPermissions.Create, TicketingPermissions.View }.Order(), names.Order());
        }
    }

    // ---------------------------------------------------------------- where access comes from

    [Fact]
    public async Task APermissionFromARole_Works_AndIsShownAsFromTheRole()
    {
        var user = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersView], viaRole: true);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);
        var entry = Entry(await AccessAsync(user), IdentityPermissions.UsersView);
        Assert.True(entry.GetProperty("grantedByRole").GetBoolean());
        Assert.False(entry.GetProperty("grantedDirectly").GetBoolean());
        Assert.True(entry.GetProperty("effective").GetBoolean());
    }

    [Fact]
    public async Task APermissionFromAGroup_Works_AndIsShownAsFromTheGroup()
    {
        var user = await host.UserAsync(host.TenantA, []);
        await GroupAsync(host.TenantA, [user.Id], [IdentityPermissions.UsersView]);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);
        var entry = Entry(await AccessAsync(user), IdentityPermissions.UsersView);
        Assert.True(entry.GetProperty("grantedByGroup").GetBoolean());
        Assert.True(entry.GetProperty("effective").GetBoolean());
    }

    // ---------------------------------------------------------------- explicit denial

    [Fact]
    public async Task DenyingARolesPermission_EndsIt_OnTheCurrentToken_AndLiftingTheDenialGivesItBack()
    {
        var user = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersView, TaskManagementPermissions.View], viaRole: true);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(user, grant: [], deny: [IdentityPermissions.UsersView])).StatusCode);

        // Same token, next request: refused, and gone from what the UI reads (/me).
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);
        Assert.DoesNotContain(IdentityPermissions.UsersView, await MyPermissionsAsync(user));
        var entry = Entry(await AccessAsync(user), IdentityPermissions.UsersView);
        Assert.True(entry.GetProperty("grantedByRole").GetBoolean());
        Assert.True(entry.GetProperty("deniedForUser").GetBoolean());
        Assert.False(entry.GetProperty("effective").GetBoolean());
        // The role itself is untouched: its other permission still works.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);

        // A client that only sends grants keeps the denial.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{user.Id}/permissions",
            new { permissionIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(user, grant: [], deny: [])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);
        Assert.Contains(IdentityPermissions.UsersView, await MyPermissionsAsync(user));
    }

    [Fact]
    public async Task ADenial_WinsOverAGroupsPermission()
    {
        var user = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        await GroupAsync(host.TenantA, [user.Id], [TaskManagementPermissions.Create]);
        Assert.Equal(HttpStatusCode.OK, (await host.CreateTaskAsync(user, "Allowed by the group")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(user, grant: [TaskManagementPermissions.View], deny: [TaskManagementPermissions.Create])).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(user, "Denied")).StatusCode);
    }

    [Fact]
    public async Task ADenial_AlsoWinsOverAPermissionImpliedByAnother()
    {
        // users.update needs users.view, so it brings it along - unless users.view is denied.
        var user = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersUpdate], viaRole: true);
        var implied = Entry(await AccessAsync(user), IdentityPermissions.UsersView);
        Assert.True(implied.GetProperty("grantedAsPrerequisite").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(user, grant: [], deny: [IdentityPermissions.UsersView])).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Get, "/api/identity/users")).StatusCode);
    }

    [Fact]
    public async Task DeniedCreateEditAndDelete_AreRefusedByTheApi()
    {
        var user = await host.UserAsync(host.TenantA,
            [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Delete], viaRole: true);
        var taskId = await host.CreatedTaskIdAsync(user, "Own task");

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(user, grant: [],
            deny: [TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Delete])).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(user, "Another")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.UpdateTaskAsync(user, taskId, "Changed")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Delete, $"/api/task-management/tasks/{taskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).StatusCode);
    }

    [Fact]
    public async Task RemovingAPermissionFromTheRole_EndsItForItsUsers_OnTheirCurrentToken()
    {
        var roleId = await RoleWithAsync(host.TenantA, TaskManagementPermissions.View, TaskManagementPermissions.Create);
        var user = await host.UserAsync(host.TenantA, []);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{user.Id}/roles", new { roleIds = new[] { roleId } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.CreateTaskAsync(user, "While the role allows it")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/roles/{roleId}/permissions",
            new { permissionIds = await host.PermissionIdsAsync(TaskManagementPermissions.View) })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(user, "After the role changed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);
    }

    // ---------------------------------------------------------------- safety rules

    [Fact]
    public async Task Denials_FollowTheEscalationRules_AndNeverReachAnotherOrganization()
    {
        // May manage users' permissions and holds task view/create - but not delete.
        var manager = await host.UserAsync(host.TenantA,
            [IdentityPermissions.UsersAssignPermissions, TaskManagementPermissions.View, TaskManagementPermissions.Create]);
        var target = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View, TaskManagementPermissions.Delete], viaRole: true);

        // Denying, or lifting a denial of, a permission the caller lacks is refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(target, grant: [], deny: [TaskManagementPermissions.Delete], by: manager)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(target, grant: [], deny: [TaskManagementPermissions.Delete])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(target, grant: [], deny: [], by: manager)).StatusCode);
        // A permission the caller holds can be denied.
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(target, grant: [], deny: [TaskManagementPermissions.Delete, TaskManagementPermissions.View], by: host.Admin)).StatusCode);

        // Not both at once; not on oneself; not in another organization.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(target, grant: [TaskManagementPermissions.Create], deny: [TaskManagementPermissions.Create])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{host.Admin.Id}/permissions",
            new { permissionIds = Array.Empty<Guid>(), deniedPermissionIds = await host.PermissionIdsAsync(IdentityPermissions.UsersView) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(target, grant: [], deny: [], by: host.AdminB)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Get, "/api/identity/users")).StatusCode);
    }

    [Fact]
    public async Task TheBuiltInSystemAdministrator_CannotBeDeniedAnything()
    {
        var systemAdmin = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersView]);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.SingleAsync(u => u.Id == systemAdmin.Id)).MarkAsSystemAccount();
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(systemAdmin, grant: [], deny: [IdentityPermissions.UsersView])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(systemAdmin, HttpMethod.Get, "/api/identity/users")).StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<HttpResponseMessage> SetAsync(AccessControlTests.Caller target, string[] grant, string[] deny, AccessControlTests.Caller? by = null) =>
        await host.SendAsync(by ?? host.Admin, HttpMethod.Put, $"/api/identity/users/{target.Id}/permissions", new
        {
            permissionIds = await host.PermissionIdsAsync(grant),
            deniedPermissionIds = await host.PermissionIdsAsync(deny),
        });

    private async Task<List<JsonElement>> AccessAsync(AccessControlTests.Caller user)
    {
        var response = await host.SendAsync(host.Admin, HttpMethod.Get, $"/api/identity/users/{user.Id}/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("permissions").EnumerateArray().ToList();
    }

    private async Task<List<string>> MyPermissionsAsync(AccessControlTests.Caller user)
    {
        var me = await JsonAsync(await host.SendAsync(user, HttpMethod.Get, "/api/identity/auth/me"));
        return me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
    }

    private static JsonElement Entry(List<JsonElement> access, string name) =>
        access.Single(entry => entry.GetProperty("name").GetString() == name);

    private async Task<Guid> RoleWithAsync(Guid tenantId, params string[] permissions)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var role = new Role(Guid.NewGuid(), tenantId, "Role " + Guid.NewGuid().ToString("N")[..6]);
        role.SetPermissions(await host.PermissionIdsAsync(permissions));
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>An organisational group (not a personal team) with these members and permissions.</summary>
    private async Task<Guid> GroupAsync(Guid tenantId, Guid[] members, string[] permissions)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var group = new UserGroup(Guid.NewGuid(), tenantId, "Group " + Guid.NewGuid().ToString("N")[..6]);
        group.SetMembers(members);
        group.SetPermissions(await host.PermissionIdsAsync(permissions));
        db.UserGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
