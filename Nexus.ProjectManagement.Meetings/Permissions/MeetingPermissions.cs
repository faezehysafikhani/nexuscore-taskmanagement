using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Meetings.Permissions;

public static class MeetingPermissions
{
    public const string View = "Meetings.View";
    public const string Create = "Meetings.Create";
    public const string Edit = "Meetings.Edit";
    public const string Delete = "Meetings.Delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Meetings", "مشاهده جلسات و صورت‌جلسات"),
        new(Create, "Meetings", "ایجاد جلسه"),
        new(Edit, "Meetings", "ویرایش جلسه، شرکت‌کنندگان، حضور و صورت‌جلسه"),
        new(Delete, "Meetings", "حذف جلسه")
    ];
}

public sealed class MeetingPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => MeetingPermissions.All;
}
