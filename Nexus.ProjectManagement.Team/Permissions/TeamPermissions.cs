using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Team.Permissions;

public static class TeamPermissions
{
    public const string View = "ProjectTeam.View";
    public const string ManageMembers = "ProjectTeam.ManageMembers";
    public const string ManageGovernance = "ProjectTeam.ManageGovernance";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectTeam", "View project team members and governance roles"),
        new(ManageMembers, "ProjectTeam", "Add or remove project team members"),
        new(ManageGovernance, "ProjectTeam", "Manage project governance roles")
    ];
}

public sealed class TeamPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => TeamPermissions.All;
}
