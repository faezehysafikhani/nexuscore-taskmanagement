using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Core.Permissions;

public static class ProjectPermissions
{
    public const string View = "Projects.View";
    public const string Create = "Projects.Create";
    public const string Edit = "Projects.Edit";
    public const string Delete = "Projects.Delete";
    public const string Submit = "Projects.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectManagement", "مشاهده پروژه‌ها"),
        new(Create, "ProjectManagement", "ایجاد پروژه"),
        new(Edit, "ProjectManagement", "ویرایش پروژه‌ها"),
        new(Delete, "ProjectManagement", "بایگانی پروژه‌ها"),
        new(Submit, "ProjectManagement", "ارسال پروژه‌ها برای تأیید")
    ];
}

public sealed class ProjectPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectPermissions.All;
}
