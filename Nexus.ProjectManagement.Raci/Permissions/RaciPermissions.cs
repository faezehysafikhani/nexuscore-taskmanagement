using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Raci.Permissions;

public static class RaciPermissions
{
    public const string View = "Raci.View";
    public const string Edit = "Raci.Edit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Raci", "مشاهده ماتریس مسئولیت‌ها (RACI)"),
        new(Edit, "Raci", "ویرایش ماتریس مسئولیت‌ها (RACI)")
    ];
}

public sealed class RaciPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => RaciPermissions.All;
}
