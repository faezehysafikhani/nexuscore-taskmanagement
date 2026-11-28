using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Core.Permissions;

public static class ProjectPermissions
{
    public const string View = "Projects.View";
    public const string Create = "Projects.Create";
    public const string Edit = "Projects.Edit";
    public const string Delete = "Projects.Delete";
    public const string Submit = "Projects.Submit";
    public const string Remove = "Projects.Remove";

    /// <summary>
    /// M7: edit, send for approval and change the status of any project of the organization - not only those one manages or owns.
    /// The built-in Administrator role gets it with every permission; give it to others explicitly.
    /// </summary>
    public const string ManageAll = "Projects.ManageAll";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectManagement", "مشاهده پروژه‌ها"),
        new(Create, "ProjectManagement", "ایجاد پروژه"),
        new(Edit, "ProjectManagement", "ویرایش پروژه‌ها"),
        new(Delete, "ProjectManagement", "بایگانی پروژه‌ها"),
        new(Submit, "ProjectManagement", "ارسال پروژه‌ها برای تأیید"),
        new(Remove, "ProjectManagement", "حذف دائمی پروژه‌ها"),
        new(ManageAll, "ProjectManagement", "مدیریت همه‌ی پروژه‌ها (ویرایش، ارسال و تغییر وضعیت پروژه‌هایی که مدیر یا مالک آن‌ها نیست)")
    ];
}

public sealed class ProjectPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectPermissions.All;
}
