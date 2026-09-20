using NexusCore.Application.Identity.Permissions;

namespace Nexus.Portfolio.Permissions;

public static class PortfolioPermissions
{
    /// <summary>Base permission: required just to call the portfolio endpoint at all. Without
    /// ViewAll, results are still filtered server-side to items the caller owns/manages.</summary>
    public const string View = "Portfolio.View";

    /// <summary>Grants seeing every project/action regardless of ownership - e.g. an
    /// organization-level viewer role. Checked at the endpoint; PortfolioService.GetPortfolioAsync
    /// only receives ViewAll=true once this has already been authorized.</summary>
    public const string ViewAll = "Portfolio.ViewAll";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Portfolio", "View the portfolio for items you own, manage, or are responsible for"),
        new(ViewAll, "Portfolio", "View the full portfolio regardless of ownership")
    ];
}

public sealed class PortfolioPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => PortfolioPermissions.All;
}
