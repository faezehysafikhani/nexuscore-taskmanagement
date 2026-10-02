using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Team.Permissions;

public static class TeamPermissions
{
    public const string View = "ProjectTeam.View";
    public const string ManageMembers = "ProjectTeam.ManageMembers";
    public const string ManageGovernance = "ProjectTeam.ManageGovernance";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectTeam", "مشاهده اعضای تیم پروژه و نقش‌های راهبری"),
        new(ManageMembers, "ProjectTeam", "افزودن یا حذف اعضای تیم پروژه"),
        new(ManageGovernance, "ProjectTeam", "مدیریت نقش‌های راهبری پروژه")
    ];
}

public sealed class TeamPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => TeamPermissions.All;
}
