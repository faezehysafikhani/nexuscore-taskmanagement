using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Kpi.Permissions;

public static class KpiPermissions
{
    public const string View = "Kpi.View";
    public const string Create = "Kpi.Create";
    public const string Edit = "Kpi.Edit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Kpi", "View KPIs"),
        new(Create, "Kpi", "Create KPIs"),
        new(Edit, "Kpi", "Edit KPIs")
    ];
}

public sealed class KpiPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => KpiPermissions.All;
}
