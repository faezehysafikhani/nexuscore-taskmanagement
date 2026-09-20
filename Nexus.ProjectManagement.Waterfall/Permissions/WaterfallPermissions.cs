using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Waterfall.Permissions;

public static class WaterfallPermissions
{
    public const string View = "WaterfallActivities.View";
    public const string Create = "WaterfallActivities.Create";
    public const string Edit = "WaterfallActivities.Edit";
    public const string Delete = "WaterfallActivities.Delete";
    public const string Submit = "WaterfallActivities.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "WaterfallPlanning", "View WBS activities"),
        new(Create, "WaterfallPlanning", "Create WBS activities"),
        new(Edit, "WaterfallPlanning", "Edit WBS activities and progress"),
        new(Delete, "WaterfallPlanning", "Delete WBS activities"),
        new(Submit, "WaterfallPlanning", "Submit activities for approval")
    ];
}

public sealed class WaterfallPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => WaterfallPermissions.All;
}
