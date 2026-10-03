using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Waterfall.Permissions;

public static class WaterfallPermissions
{
    public const string View = "WaterfallActivities.View";
    public const string Create = "WaterfallActivities.Create";
    public const string Edit = "WaterfallActivities.Edit";
    public const string Delete = "WaterfallActivities.Delete";
    public const string Submit = "WaterfallActivities.Submit";

    /// <summary>Dependencies, schedule calculation and its application, baselines, progress
    /// snapshots and MS Project import: everything that shapes the plan rather than one activity.</summary>
    public const string ManageSchedule = "WaterfallSchedule.Manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "WaterfallPlanning", "مشاهده فعالیت‌های ساختار شکست کار"),
        new(Create, "WaterfallPlanning", "ایجاد فعالیت ساختار شکست کار"),
        new(Edit, "WaterfallPlanning", "ویرایش فعالیت‌ها و پیشرفت ساختار شکست کار"),
        new(Delete, "WaterfallPlanning", "حذف فعالیت‌های ساختار شکست کار"),
        new(Submit, "WaterfallPlanning", "ارسال فعالیت‌ها برای تأیید"),
        new(ManageSchedule, "WaterfallPlanning", "مدیریت زمان‌بندی: وابستگی‌ها، محاسبه برنامه، خط مبنا و اسنپ‌شات پیشرفت")
    ];
}

public sealed class WaterfallPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => WaterfallPermissions.All;
}
