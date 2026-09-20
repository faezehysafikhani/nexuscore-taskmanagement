using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.ProjectWorkflow.Permissions;

public static class ProjectWorkflowPermissions
{
    public const string Configure = "ProjectWorkflow.Configure";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Configure, "ProjectWorkflow", "Configure project-specific workflow overrides")
    ];
}

public sealed class ProjectWorkflowPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectWorkflowPermissions.All;
}
