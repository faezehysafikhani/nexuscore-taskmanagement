using NexusCore.Application.Identity.Permissions;

namespace Nexus.TaskManagement.Permissions;

public static class TaskManagementPermissions
{
    public const string View = "Tasks.View";
    public const string Create = "Tasks.Create";
    public const string Edit = "Tasks.Edit";
    public const string Delete = "Tasks.Delete";
    public const string Assign = "Tasks.Assign";
    public const string ManageRecurring = "Tasks.ManageRecurring";
    public const string ManageTags = "Tasks.ManageTags";
    public const string UploadFiles = "Tasks.UploadFiles";
    public const string ManageNotes = "Notes.Manage";
    public const string Comment = "Tasks.Comment";

    /// <summary>
    /// See and manage every task of the organization. Without it a user sees only the tasks they
    /// own, are assigned to (directly, as a collaborator or through a team), and changes the
    /// details of their own tasks only.
    /// </summary>
    public const string ManageAll = "Tasks.ManageAll";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "TaskManagement", "مشاهده وظایف"),
        new(Create, "TaskManagement", "ایجاد وظیفه"),
        new(Edit, "TaskManagement", "ویرایش وظیفه"),
        new(Delete, "TaskManagement", "حذف وظیفه"),
        new(Assign, "TaskManagement", "واگذاری وظیفه به کاربران و تیم‌ها"),
        new(ManageRecurring, "TaskManagement", "مدیریت زمان‌بندی وظایف تکرارشونده"),
        new(ManageTags, "TaskManagement", "مدیریت برچسب‌های وظایف"),
        new(UploadFiles, "TaskManagement", "بارگذاری و حذف پیوست وظایف"),
        new(ManageNotes, "TaskManagement", "مدیریت یادداشت‌های شخصی"),
        new(Comment, "TaskManagement", "ثبت نظر روی وظایف"),
        new(ManageAll, "TaskManagement", "مشاهده و مدیریت همه وظایف سازمان")
    ];
}

public sealed class TaskManagementPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => TaskManagementPermissions.All;
}
