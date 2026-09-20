using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Agile.Permissions;

public static class AgilePermissions
{
    public const string View = "AgileTasks.View";
    public const string Create = "AgileTasks.Create";
    public const string Edit = "AgileTasks.Edit";
    public const string Delete = "AgileTasks.Delete";
    public const string Submit = "AgileTasks.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "AgilePlanning", "View the agile task board"),
        new(Create, "AgilePlanning", "Create agile tasks"),
        new(Edit, "AgilePlanning", "Edit agile tasks"),
        new(Delete, "AgilePlanning", "Delete agile tasks"),
        new(Submit, "AgilePlanning", "Submit agile tasks for approval")
    ];
}

public sealed class AgilePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => AgilePermissions.All;
}
