using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Progress.Permissions;

public static class ProgressPermissions
{
    public const string View = "ProjectProgress.View";
    public const string Create = "ProjectProgress.Create";
    public const string Edit = "ProjectProgress.Edit";
    public const string Submit = "ProjectProgress.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProgressManagement", "View project status updates"),
        new(Create, "ProgressManagement", "Create project status updates"),
        new(Edit, "ProgressManagement", "Edit project status updates"),
        new(Submit, "ProgressManagement", "Submit status updates for approval")
    ];
}

public sealed class ProgressPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProgressPermissions.All;
}
