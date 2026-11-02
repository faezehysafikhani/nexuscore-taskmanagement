using NexusCore.Application.Identity.Permissions;

namespace Nexus.Calendar.Permissions;

public static class CalendarPermissions
{
    public const string View = "work_calendars.view";
    public const string Create = "work_calendars.create";
    public const string Update = "work_calendars.update";

    public const string ViewMeetings = "Meetings.View";
    public const string CreateMeetings = "Meetings.Create";
    public const string EditMeetings = "Meetings.Edit";
    public const string DeleteMeetings = "Meetings.Delete";
    public const string InviteHigherToMeetings = "Meetings.InviteHigher";
    public const string AccessAllMeetingPeople = "Meetings.AccessAllPeople";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Calendar", "مشاهده تقویم‌های کاری"),
        new(Create, "Calendar", "ایجاد تقویم کاری"),
        new(Update, "Calendar", "ویرایش تقویم‌های کاری و استثناهای آن‌ها"),
        new(ViewMeetings, "Meetings", "مشاهده جلسات در محدوده مجاز چارت سازمانی"),
        new(CreateMeetings, "Meetings", "ایجاد جلسه"),
        new(EditMeetings, "Meetings", "ویرایش جلسه‌های مجاز"),
        new(DeleteMeetings, "Meetings", "حذف جلسه‌های مجاز"),
        new(InviteHigherToMeetings, "Meetings", "دعوت از بالادست‌های زنجیره سازمانی خود در جلسه"),
        new(AccessAllMeetingPeople, "Meetings", "دسترسی به همه افراد و همه جلسات سازمان در بخش جلسات")
    ];
}

/// <summary>
/// Compatibility names for hosts that already use the public Meetings.* permission identifiers.
/// </summary>
public static class MeetingPermissions
{
    public const string View = CalendarPermissions.ViewMeetings;
    public const string Create = CalendarPermissions.CreateMeetings;
    public const string Edit = CalendarPermissions.EditMeetings;
    public const string Delete = CalendarPermissions.DeleteMeetings;
    public const string InviteHigher = CalendarPermissions.InviteHigherToMeetings;
    public const string AccessAllPeople = CalendarPermissions.AccessAllMeetingPeople;
}

public sealed class CalendarPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => CalendarPermissions.All;
}
