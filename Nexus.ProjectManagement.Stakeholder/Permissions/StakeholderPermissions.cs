using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.StakeholderManagement.Permissions;

public static class StakeholderPermissions
{
    public const string View = "Stakeholders.View";
    public const string Create = "Stakeholders.Create";
    public const string Edit = "Stakeholders.Edit";
    public const string Delete = "Stakeholders.Delete";
    public const string Submit = "Stakeholders.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "StakeholderManagement", "View the stakeholder register"),
        new(Create, "StakeholderManagement", "Create stakeholders"),
        new(Edit, "StakeholderManagement", "Edit stakeholders"),
        new(Delete, "StakeholderManagement", "Delete stakeholders"),
        new(Submit, "StakeholderManagement", "Submit stakeholders for approval")
    ];
}

public sealed class StakeholderPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => StakeholderPermissions.All;
}
