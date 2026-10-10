using NexusCore.Application.Identity.Permissions;

namespace Nexus.Calendar.Permissions;

public static class CalendarPermissions
{
    public const string View = "work_calendars.view";
    public const string Create = "work_calendars.create";
    public const string Update = "work_calendars.update";
    public const string Delete = "work_calendars.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Calendar", "مشاهده تقویم‌های کاری"),
        new(Create, "Calendar", "ایجاد تقویم کاری"),
        new(Update, "Calendar", "ویرایش تقویم‌های کاری و استثناهای آن‌ها"),
        new(Delete, "Calendar", "حذف تقویم کاری")
    ];
}

public sealed class CalendarPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => CalendarPermissions.All;
}
