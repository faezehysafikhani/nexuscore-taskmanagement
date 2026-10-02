using NexusCore.Application.Identity.Permissions;

namespace Nexus.Organization.Permissions;

public static class OrganizationPermissions
{
    public const string View = "organization_units.view";
    public const string Create = "organization_units.create";
    public const string Update = "organization_units.update";
    public const string Delete = "organization_units.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Organization", "مشاهده واحدهای سازمانی"),
        new(Create, "Organization", "ایجاد واحد سازمانی"),
        new(Update, "Organization", "ویرایش واحدهای سازمانی"),
        new(Delete, "Organization", "غیرفعال‌سازی واحدهای سازمانی")
    ];
}

public sealed class OrganizationPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => OrganizationPermissions.All;
}
