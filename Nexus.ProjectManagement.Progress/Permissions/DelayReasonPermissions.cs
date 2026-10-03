using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Progress.Permissions;

public static class DelayReasonPermissions
{
    public const string View = "DelayReasons.View";
    public const string Create = "DelayReasons.Create";
    public const string Edit = "DelayReasons.Edit";
    public const string Delete = "DelayReasons.Delete";
    public const string Submit = "DelayReasons.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProgressManagement", "مشاهده دلایل تأخیر پروژه"),
        new(Create, "ProgressManagement", "ثبت دلیل تأخیر پروژه"),
        new(Edit, "ProgressManagement", "ویرایش دلایل تأخیر پروژه"),
        new(Delete, "ProgressManagement", "حذف دلایل تأخیر پروژه"),
        new(Submit, "ProgressManagement", "ارسال دلایل تأخیر برای تأیید")
    ];
}

public sealed class DelayReasonPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => DelayReasonPermissions.All;
}
