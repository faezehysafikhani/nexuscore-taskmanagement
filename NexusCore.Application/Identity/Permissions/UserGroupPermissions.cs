namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Permission catalogue for the optional user-group feature.
/// Kept separate from <see cref="IdentityPermissions"/> so the whole feature can be
/// removed by deleting this file and the single Concat in IdentityPermissions.All.
/// </summary>
public static class UserGroupPermissions
{
    public const string GroupsView = "groups.view";
    public const string GroupsCreate = "groups.create";
    public const string GroupsUpdate = "groups.update";
    public const string GroupsAssignPermissions = "groups.assign_permissions";
    public const string GroupsManageMembers = "groups.manage_members";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(GroupsView, "Identity", "View user groups"),
        new(GroupsCreate, "Identity", "Create user groups"),
        new(GroupsUpdate, "Identity", "Update user groups"),
        new(GroupsAssignPermissions, "Identity", "Assign permissions to user groups"),
        new(GroupsManageMembers, "Identity", "Add or remove group members")
    ];
}
