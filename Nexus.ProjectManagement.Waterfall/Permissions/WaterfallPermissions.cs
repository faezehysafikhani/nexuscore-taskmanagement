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
        new(View, "WaterfallPlanning", "مشاهده فعالیت‌های ساختار شکست کار"),
        new(Create, "WaterfallPlanning", "ایجاد فعالیت ساختار شکست کار"),
        new(Edit, "WaterfallPlanning", "ویرایش فعالیت‌ها و پیشرفت ساختار شکست کار"),
        new(Delete, "WaterfallPlanning", "حذف فعالیت‌های ساختار شکست کار"),
        new(Submit, "WaterfallPlanning", "ارسال فعالیت‌ها برای تأیید")
    ];
}

public sealed class WaterfallPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => WaterfallPermissions.All;
}
