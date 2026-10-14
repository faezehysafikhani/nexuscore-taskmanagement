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

    /// <summary>
    /// See the "my work team" section: the teams one belongs to and their members, read only.
    /// Without it (or GroupsManageOwn, which implies it) the section is not shown at all.
    /// Listing one's own teams stays open to every user on the server (GET /groups/mine) -
    /// chat needs it to show a member their team conversation.
    /// </summary>
    public const string GroupsViewOwn = "groups.view_own";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(GroupsView, "Identity", "مشاهده گروه‌های کاربری"),
        new(GroupsCreate, "Identity", "تعریف گروه کاربری جدید"),
        new(GroupsUpdate, "Identity", "ویرایش گروه‌های کاربری"),
        new(GroupsAssignPermissions, "Identity", "تعیین دسترسی‌های گروه‌های کاربری"),
        new(GroupsManageMembers, "Identity", "تعیین اعضای گروه‌های کاربری"),
        new(GroupsDelete, "Identity", "حذف گروه‌های کاربری"),
        new(GroupsViewOwn, "Identity", "مشاهده بخش تیم کاری من"),
        new(GroupsManageOwn, "Identity", "ساخت و مدیریت تیم‌های کاری")
    ];
}
