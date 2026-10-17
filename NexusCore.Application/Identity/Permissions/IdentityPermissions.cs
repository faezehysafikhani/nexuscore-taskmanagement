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

    /// <summary>
    /// Platform-wide administration: acting on users, roles, groups, settings and audit logs of
    /// organizations other than one's own. Everyone else is confined to their own organization.
    /// </summary>
    public const string TenantsManageAll = "tenants.manage_all";
    public const string AuditLogsView = "audit_logs.view";
    public const string SettingsView = "settings.view";
    public const string SettingsUpdate = "settings.update";
    public const string SmsSettingsView = "sms_settings.view";
    public const string SmsSettingsUpdate = "sms_settings.update";
    public const string SmsSettingsTest = "sms_settings.test";
    public const string LdapSettingsView = "ldap_settings.view";
    public const string LdapSettingsUpdate = "ldap_settings.update";
    public const string LdapSettingsTest = "ldap_settings.test";
    public const string SsoSettingsView = "sso_settings.view";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersView, "Identity", "مشاهده فهرست کاربران"),
        new(UsersCreate, "Identity", "افزودن کاربر جدید"),
        new(UsersUpdate, "Identity", "ویرایش اطلاعات کاربران"),
        new(UsersDelete, "Identity", "حذف کاربران"),
        new(UsersAssignRoles, "Identity", "تعیین نقش کاربران"),
        new(UsersAssignPermissions, "Identity", "تعیین دسترسی‌های مستقیم کاربران"),
        new(UsersChangeStatus, "Identity", "فعال و غیرفعال کردن حساب کاربران"),
        new(RolesView, "Identity", "مشاهده نقش‌ها"),
        new(RolesCreate, "Identity", "تعریف نقش جدید"),
        new(RolesUpdate, "Identity", "ویرایش نقش‌ها"),
        new(RolesAssignPermissions, "Identity", "تعیین دسترسی‌های هر نقش"),
        new(PermissionsView, "Identity", "مشاهده فهرست دسترسی‌ها"),
        new(TenantsView, "Platform", "مشاهده سازمان‌ها"),
        new(TenantsCreate, "Platform", "تعریف سازمان جدید"),
        new(TenantsManageAll, "Platform", "مدیریت همه سازمان‌ها (دسترسی فراسازمانی)"),
        new(AuditLogsView, "Platform", "مشاهده گزارش رویدادها و تاریخچه ورود"),
        new(SettingsView, "Platform", "مشاهده تنظیمات عمومی سامانه"),
        new(SettingsUpdate, "Platform", "ویرایش تنظیمات عمومی سامانه"),
        new(SmsSettingsView, "Platform", "مشاهده پنل پیامکی (تنظیمات و متن پیامک‌ها)"),
        new(SmsSettingsUpdate, "Platform", "ویرایش پنل پیامکی (تنظیمات و متن پیامک‌ها)"),
        new(SmsSettingsTest, "Platform", "ارسال پیامک آزمایشی"),
        new(LdapSettingsView, "Platform", "مشاهده تنظیمات اتصال به LDAP"),
        new(LdapSettingsUpdate, "Platform", "ویرایش تنظیمات اتصال به LDAP"),
        new(LdapSettingsTest, "Platform", "آزمایش اتصال به LDAP"),
        new(SsoSettingsView, "Platform", "مشاهده تنظیمات ورود یکپارچه (SSO)")
    ];
}

public sealed record PermissionDefinition(string Name, string Module, string Description);
