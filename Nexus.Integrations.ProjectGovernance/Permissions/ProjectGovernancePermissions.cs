using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.ProjectGovernance.Permissions;

public static class ProjectGovernancePermissions
{
    public const string Configure = "ProjectGovernance.Configure";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Configure, "ProjectGovernance", "پیکربندی قوانین دید و حاکمیت پروژه‌ها")
    ];
}

public sealed class ProjectGovernancePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectGovernancePermissions.All;
}
