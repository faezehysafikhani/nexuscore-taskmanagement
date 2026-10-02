using NexusCore.Application.Identity.Permissions;

namespace Nexus.StrategyManagement.Permissions;

public static class StrategyPermissions
{
    public const string View = "Strategy.View";
    public const string Create = "Strategy.Create";
    public const string Edit = "Strategy.Edit";
    public const string Delete = "Strategy.Delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Strategy", "مشاهده درخت راهبرد"),
        new(Create, "Strategy", "ایجاد راهبرد"),
        new(Edit, "Strategy", "ویرایش راهبردها"),
        new(Delete, "Strategy", "حذف راهبردها")
    ];
}

public sealed class StrategyPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => StrategyPermissions.All;
}
