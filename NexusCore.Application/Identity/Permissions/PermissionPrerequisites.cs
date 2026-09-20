namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Some permissions are useless on their own: you cannot manage the members of groups you
/// are not allowed to list, and you cannot pick members from a user list you cannot read.
///
/// Granting an action therefore implicitly grants the read permissions it depends on.
/// The closure is applied once, when effective permissions are resolved, so the JWT already
/// carries everything the endpoints will ask for.
///
/// Implied permissions are NOT stored anywhere - assignments stay exactly as the admin made
/// them, and /me returns the un-expanded set separately so the UI can show only the sections
/// the user was actually given.
/// </summary>
public static class PermissionPrerequisites
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {
        [IdentityPermissions.UsersCreate] = [IdentityPermissions.UsersView],
        [IdentityPermissions.UsersUpdate] = [IdentityPermissions.UsersView],
        [IdentityPermissions.UsersAssignRoles] = [IdentityPermissions.UsersView, IdentityPermissions.RolesView],
        [IdentityPermissions.UsersAssignPermissions] = [IdentityPermissions.UsersView, IdentityPermissions.PermissionsView],

        [IdentityPermissions.RolesCreate] = [IdentityPermissions.RolesView],
        [IdentityPermissions.RolesUpdate] = [IdentityPermissions.RolesView],
        [IdentityPermissions.RolesAssignPermissions] = [IdentityPermissions.RolesView, IdentityPermissions.PermissionsView],

        [IdentityPermissions.SettingsUpdate] = [IdentityPermissions.SettingsView],
        [IdentityPermissions.TenantsCreate] = [IdentityPermissions.TenantsView],

        // Optional user-group feature. Remove these four entries together with the feature.
        [UserGroupPermissions.GroupsCreate] = [UserGroupPermissions.GroupsView],
        [UserGroupPermissions.GroupsUpdate] = [UserGroupPermissions.GroupsView],
        [UserGroupPermissions.GroupsAssignPermissions] = [UserGroupPermissions.GroupsView, IdentityPermissions.PermissionsView],
        [UserGroupPermissions.GroupsManageMembers] = [UserGroupPermissions.GroupsView, IdentityPermissions.UsersView]
    };

    /// <summary>
    /// Returns the granted permissions plus everything they transitively depend on.
    /// Safe against cycles and against permissions that have no prerequisites.
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> granted)
    {
        var effective = new HashSet<string>(granted, StringComparer.Ordinal);
        var pending = new Queue<string>(effective);

        while (pending.Count > 0)
        {
            if (!Map.TryGetValue(pending.Dequeue(), out var prerequisites))
            {
                continue;
            }

            foreach (var prerequisite in prerequisites)
            {
                // Only walk a prerequisite the first time it is added, so a cycle terminates.
                if (effective.Add(prerequisite))
                {
                    pending.Enqueue(prerequisite);
                }
            }
        }

        return effective.OrderBy(permission => permission, StringComparer.Ordinal).ToList();
    }
}
