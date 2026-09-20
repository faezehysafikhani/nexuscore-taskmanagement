using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.RiskManagement.Permissions;

public static class RiskPermissions
{
    public const string View = "Risks.View";
    public const string Create = "Risks.Create";
    public const string Edit = "Risks.Edit";
    public const string Delete = "Risks.Delete";
    public const string Submit = "Risks.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "RiskManagement", "View the risk register"),
        new(Create, "RiskManagement", "Create risks"),
        new(Edit, "RiskManagement", "Edit risks"),
        new(Delete, "RiskManagement", "Delete risks"),
        new(Submit, "RiskManagement", "Submit risks for approval")
    ];
}

public sealed class RiskPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => RiskPermissions.All;
}
