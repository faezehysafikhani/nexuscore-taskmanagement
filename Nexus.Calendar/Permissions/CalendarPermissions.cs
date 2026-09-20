using NexusCore.Application.Identity.Permissions;

namespace Nexus.Calendar.Permissions;

public static class CalendarPermissions
{
    public const string View = "work_calendars.view";
    public const string Create = "work_calendars.create";
    public const string Update = "work_calendars.update";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Calendar", "View work calendars"),
        new(Create, "Calendar", "Create work calendars"),
        new(Update, "Calendar", "Update work calendars and their exceptions")
    ];
}

public sealed class CalendarPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => CalendarPermissions.All;
}
