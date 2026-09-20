namespace NexusCore.Application.Identity.Permissions;

public static class IdentityPermissions
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string UsersAssignPermissions = "users.assign_permissions";
    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesAssignPermissions = "roles.assign_permissions";
    public const string PermissionsView = "permissions.view";
    public const string TenantsView = "tenants.view";
    public const string TenantsCreate = "tenants.create";
    public const string AuditLogsView = "audit_logs.view";
    public const string SettingsView = "settings.view";
    public const string SettingsUpdate = "settings.update";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersView, "Identity", "View users"),
        new(UsersCreate, "Identity", "Create users"),
        new(UsersUpdate, "Identity", "Update users"),
        new(UsersDelete, "Identity", "Delete users"),
        new(UsersAssignRoles, "Identity", "Assign roles to users"),
        new(UsersAssignPermissions, "Identity", "Assign permissions to roles"),
        new(RolesView, "Identity", "View roles"),
        new(RolesCreate, "Identity", "Create roles"),
        new(RolesUpdate, "Identity", "Update roles"),
        new(RolesAssignPermissions, "Identity", "Assign permissions to roles"),
        new(PermissionsView, "Identity", "View permissions"),
        new(TenantsView, "Platform", "View tenants"),
        new(TenantsCreate, "Platform", "Create tenants"),
        new(AuditLogsView, "Platform", "View audit logs"),
        new(SettingsView, "Platform", "View settings"),
        new(SettingsUpdate, "Platform", "Update settings")
    ];
}

public sealed record PermissionDefinition(string Name, string Module, string Description);
