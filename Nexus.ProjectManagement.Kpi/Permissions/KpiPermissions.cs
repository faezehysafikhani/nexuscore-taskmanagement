using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Kpi.Permissions;

public static class KpiPermissions
{
    public const string View = "Kpi.View";
    public const string Create = "Kpi.Create";
    public const string Edit = "Kpi.Edit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Kpi", "مشاهده شاخص‌های کلیدی عملکرد"),
        new(Create, "Kpi", "ایجاد شاخص کلیدی عملکرد"),
        new(Edit, "Kpi", "ویرایش شاخص‌های کلیدی عملکرد")
    ];
}

public sealed class KpiPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => KpiPermissions.All;
}
