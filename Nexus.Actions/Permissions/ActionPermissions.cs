using NexusCore.Application.Identity.Permissions;

namespace Nexus.Actions.Permissions;

public static class ActionPermissions
{
    public const string View = "Actions.View";
    public const string Create = "Actions.Create";
    public const string Edit = "Actions.Edit";
    public const string Delete = "Actions.Delete";
    public const string Submit = "Actions.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Actions", "View actions"),
        new(Create, "Actions", "Create actions"),
        new(Edit, "Actions", "Edit actions"),
        new(Delete, "Actions", "Cancel actions"),
        new(Submit, "Actions", "Submit actions for approval")
    ];
}

public sealed class ActionPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ActionPermissions.All;
}
