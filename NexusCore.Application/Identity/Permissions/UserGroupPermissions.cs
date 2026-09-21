namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Permission catalogue for the optional user-group feature.
/// Kept separate from <see cref="IdentityPermissions"/> so the whole feature can be removed
/// with it. These permissions are catalogued (UserGroupPermissionCatalog) and registered as
/// authorization policies by AddUserGroupFeature - the same place the feature's services are
/// registered - so they exist exactly when the group endpoints do.
/// </summary>
public static class UserGroupPermissions
{
    public const string GroupsView = "groups.view";
    public const string GroupsCreate = "groups.create";
    public const string GroupsUpdate = "groups.update";
    public const string GroupsAssignPermissions = "groups.assign_permissions";
    public const string GroupsManageMembers = "groups.manage_members";
    public const string GroupsDelete = "groups.delete";

    /// <summary>
    /// Create and maintain one's own work teams (groups the caller created). Does not allow
    /// touching anyone else's group or assigning permissions to any group.
    /// </summary>
    public const string GroupsManageOwn = "groups.manage_own";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(GroupsView, "Identity", "View user groups"),
        new(GroupsCreate, "Identity", "Create user groups"),
        new(GroupsUpdate, "Identity", "Update user groups"),
        new(GroupsAssignPermissions, "Identity", "Assign permissions to user groups"),
        new(GroupsManageMembers, "Identity", "Add or remove group members"),
        new(GroupsDelete, "Identity", "Delete user groups"),
        new(GroupsManageOwn, "Identity", "Create and manage own work teams")
    ];
}
