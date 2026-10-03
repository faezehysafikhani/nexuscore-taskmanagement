using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.History.Permissions;

public static class ProjectHistoryPermissions
{
    public const string View = "ProjectHistory.View";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectHistory", "مشاهده تاریخچه تغییرات پروژه")
    ];
}

public sealed class ProjectHistoryPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectHistoryPermissions.All;
}
