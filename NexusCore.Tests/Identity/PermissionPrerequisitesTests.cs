using NexusCore.Application.Identity.Permissions;

namespace NexusCore.Tests.Identity;

/// <summary>
/// The dependency table of permissions. The same table lives in the web client
/// (src/utils/permissions.ts, checked by scripts/check-permission-prerequisites.mjs); keep the two
/// in step - this snapshot is what both are compared with.
/// </summary>
public sealed class PermissionPrerequisitesTests
{
    // Dependent permission -> prerequisites every product must grant with it.
    private static readonly Dictionary<string, string[]> Expected = new(StringComparer.Ordinal)
    {
        ["users.create"] = ["users.view"],
        ["users.update"] = ["users.view"],
        ["users.delete"] = ["users.view"],
        ["users.change_status"] = ["users.view"],
        ["users.assign_roles"] = ["users.view", "roles.view"],
        ["users.assign_permissions"] = ["users.view", "permissions.view"],
        ["roles.create"] = ["roles.view"],
        ["roles.update"] = ["roles.view"],
        ["roles.assign_permissions"] = ["roles.view", "permissions.view"],
        ["groups.create"] = ["groups.view"],
        ["groups.update"] = ["groups.view"],
        ["groups.delete"] = ["groups.view"],
        ["groups.manage_members"] = ["groups.view", "users.view"],
        ["groups.assign_permissions"] = ["groups.view", "permissions.view"],
        // The server also asks for users.view here: a team is built from the user list.
        ["groups.manage_own"] = ["groups.view_own", "users.view"],
        ["organization_units.create"] = ["organization_units.view"],
        ["organization_units.update"] = ["organization_units.view"],
        ["organization_units.delete"] = ["organization_units.view"],
        ["org_structure.manage_companies"] = ["org_structure.view", "users.view", "users.update"],
        ["org_structure.manage_positions"] = ["org_structure.view", "users.view", "users.update"],
        ["org_structure.manage_assignments"] = ["org_structure.view", "users.view", "users.update"],
        ["org_structure.manage_substitution"] = ["org_structure.view"],
        ["org_structure.assign_tasks_any_level"] = ["org_structure.view"],
        ["org_structure.team_members_any_unit"] = ["org_structure.view"],
        ["org_structure.design_charts"] = ["org_structure.view"],
        ["settings.update"] = ["settings.view"],
        ["sms_settings.update"] = ["sms_settings.view"],
        ["sms_settings.test"] = ["sms_settings.view"],
        ["ldap_settings.update"] = ["ldap_settings.view"],
        ["ldap_settings.test"] = ["ldap_settings.view"],
        ["tenants.create"] = ["tenants.view"],
        ["tenants.manage_all"] = ["tenants.view"],
        ["Tasks.Create"] = ["Tasks.View"],
        ["Tasks.Edit"] = ["Tasks.View"],
        ["Tasks.Delete"] = ["Tasks.View"],
        ["Tasks.Assign"] = ["Tasks.View"],
        ["Tasks.Comment"] = ["Tasks.View"],
        ["Tasks.UploadFiles"] = ["Tasks.View"],
        ["Tasks.ManageAll"] = ["Tasks.View"],
        ["Tasks.ManageRecurring"] = ["Tasks.View"],
        ["Tasks.ManageTags"] = ["Tasks.View"],
    };

    private static readonly string[] Base =
    [
        "users.view", "roles.view", "permissions.view", "groups.view", "groups.view_own", "org_structure.view",
        "organization_units.view", "audit_logs.view", "settings.view", "sms_settings.view", "ldap_settings.view",
        "sso_settings.view", "tenants.view", "Tasks.View", "Notes.Manage",
    ];

    [Fact]
    public void Every_dependent_permission_carries_its_prerequisites()
    {
        foreach (var (permission, prerequisites) in Expected)
        {
            Assert.True(PermissionPrerequisites.All.TryGetValue(permission, out var actual), $"{permission} has no prerequisites listed");
            Assert.True(prerequisites.All(actual!.Contains), $"{permission}: expected {string.Join(", ", prerequisites)}, got {string.Join(", ", actual)}");
        }
    }

    [Fact]
    public void Base_permissions_depend_on_nothing()
    {
        foreach (var permission in Base)
        {
            Assert.False(PermissionPrerequisites.All.ContainsKey(permission), $"{permission} must not have prerequisites");
        }
    }

    [Fact]
    public void Expand_adds_prerequisites_and_ImpliedBy_lists_only_the_missing_ones()
    {
        var expanded = PermissionPrerequisites.Expand(["users.assign_roles"]);
        Assert.Contains("users.view", expanded);
        Assert.Contains("roles.view", expanded);

        var implied = PermissionPrerequisites.ImpliedBy(["users.assign_roles", "users.view"]);
        Assert.Equal(new[] { "roles.view" }, implied);
        Assert.Empty(PermissionPrerequisites.ImpliedBy(["users.view"]));
    }
}
