using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Core.Permissions;

public static class ProjectPermissions
{
    public const string View = "Projects.View";
    public const string Create = "Projects.Create";
    public const string Edit = "Projects.Edit";
    public const string Delete = "Projects.Delete";
    public const string Submit = "Projects.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectManagement", "View projects"),
        new(Create, "ProjectManagement", "Create projects"),
        new(Edit, "ProjectManagement", "Edit projects"),
        new(Delete, "ProjectManagement", "Archive projects"),
        new(Submit, "ProjectManagement", "Submit projects for approval")
    ];
}

public sealed class ProjectPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectPermissions.All;
}
