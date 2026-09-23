using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Progress.Permissions;

public static class ProgressPermissions
{
    public const string View = "ProjectProgress.View";
    public const string Create = "ProjectProgress.Create";
    public const string Edit = "ProjectProgress.Edit";
    public const string Submit = "ProjectProgress.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProgressManagement", "مشاهده گزارش‌های وضعیت پروژه"),
        new(Create, "ProgressManagement", "ایجاد گزارش وضعیت پروژه"),
        new(Edit, "ProgressManagement", "ویرایش گزارش‌های وضعیت پروژه"),
        new(Submit, "ProgressManagement", "ارسال گزارش‌های وضعیت برای تأیید")
    ];
}

public sealed class ProgressPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProgressPermissions.All;
}
