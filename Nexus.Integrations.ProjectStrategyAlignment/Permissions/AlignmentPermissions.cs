using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.StrategyAlignment.Permissions;

public static class AlignmentPermissions
{
    public const string View = "ProjectStrategyAlignment.View";
    public const string Manage = "ProjectStrategyAlignment.Manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectStrategyAlignment", "مشاهده ماتریس هم‌راستایی پروژه و راهبرد"),
        new(Manage, "ProjectStrategyAlignment", "ایجاد یا ویرایش هم‌راستایی پروژه و راهبرد")
    ];
}

public sealed class AlignmentPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => AlignmentPermissions.All;
}
