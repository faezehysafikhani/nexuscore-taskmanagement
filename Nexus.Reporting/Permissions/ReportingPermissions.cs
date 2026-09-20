using NexusCore.Application.Identity.Permissions;

namespace Nexus.Reporting.Permissions;

public static class ReportingPermissions
{
    /// <summary>Base permission: view your own dashboard (projects/actions you own, manage, or
    /// are responsible for). Every authenticated user with this permission can call /me.</summary>
    public const string View = "Reporting.View";

    /// <summary>Grants the tenant/organization-unit-wide summary dashboard and any single
    /// project's dashboard, regardless of ownership - mirrors Portfolio.ViewAll. Checked at the
    /// endpoint, same as Portfolio: real backend gating, not UI hiding.</summary>
    public const string ViewAll = "Reporting.ViewAll";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Reporting", "View your own dashboard"),
        new(ViewAll, "Reporting", "View tenant/organization-wide summaries and any project's dashboard")
    ];
}

public sealed class ReportingPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ReportingPermissions.All;
}
