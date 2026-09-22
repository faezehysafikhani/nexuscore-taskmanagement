namespace NexusCore.Application.Identity.Permissions;

public static class IdentityPermissions
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string UsersAssignPermissions = "users.assign_permissions";
    public const string UsersChangeStatus = "users.change_status";
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
    public const string SmsSettingsView = "sms_settings.view";
    public const string SmsSettingsUpdate = "sms_settings.update";
    public const string SmsSettingsTest = "sms_settings.test";
    public const string LdapSettingsView = "ldap_settings.view";
    public const string LdapSettingsUpdate = "ldap_settings.update";
    public const string LdapSettingsTest = "ldap_settings.test";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersView, "Identity", "View users"),
        new(UsersCreate, "Identity", "Create users"),
        new(UsersUpdate, "Identity", "Update users"),
        new(UsersDelete, "Identity", "Delete users"),
        new(UsersAssignRoles, "Identity", "Assign roles to users"),
        new(UsersAssignPermissions, "Identity", "Grant permissions directly to users"),
        new(UsersChangeStatus, "Identity", "Enable or disable users"),
        new(RolesView, "Identity", "View roles"),
        new(RolesCreate, "Identity", "Create roles"),
        new(RolesUpdate, "Identity", "Update roles"),
        new(RolesAssignPermissions, "Identity", "Assign permissions to roles"),
        new(PermissionsView, "Identity", "View permissions"),
        new(TenantsView, "Platform", "View tenants"),
        new(TenantsCreate, "Platform", "Create tenants"),
        new(AuditLogsView, "Platform", "View audit logs"),
        new(SettingsView, "Platform", "View settings"),
        new(SettingsUpdate, "Platform", "Update settings"),
        new(SmsSettingsView, "Platform", "View the SMS panel settings and templates"),
        new(SmsSettingsUpdate, "Platform", "Change the SMS panel settings and templates"),
        new(SmsSettingsTest, "Platform", "Send test SMS messages"),
        new(LdapSettingsView, "Platform", "View the LDAP settings"),
        new(LdapSettingsUpdate, "Platform", "Change the LDAP settings"),
        new(LdapSettingsTest, "Platform", "Test the LDAP connection")
    ];
}

public sealed record PermissionDefinition(string Name, string Module, string Description);
