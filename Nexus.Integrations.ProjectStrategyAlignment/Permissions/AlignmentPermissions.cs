using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.StrategyAlignment.Permissions;

public static class AlignmentPermissions
{
    public const string View = "ProjectStrategyAlignment.View";
    public const string Manage = "ProjectStrategyAlignment.Manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectStrategyAlignment", "View the project x strategy alignment matrix"),
        new(Manage, "ProjectStrategyAlignment", "Create or edit project x strategy alignments")
    ];
}

public sealed class AlignmentPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => AlignmentPermissions.All;
}
