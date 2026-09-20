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

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "TaskManagement", "View tasks and projects"),
        new(Create, "TaskManagement", "Create tasks and projects"),
        new(Edit, "TaskManagement", "Edit tasks and projects"),
        new(Delete, "TaskManagement", "Delete tasks and projects"),
        new(Assign, "TaskManagement", "Assign tasks to users and groups"),
        new(ManageRecurring, "TaskManagement", "Manage recurring task schedules"),
        new(ManageTags, "TaskManagement", "Manage task tags"),
        new(UploadFiles, "TaskManagement", "Upload task attachments"),
        new(ManageNotes, "TaskManagement", "Manage personal notes"),
        new(Comment, "TaskManagement", "Comment on tasks")
    ];
}

public sealed class TaskManagementPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => TaskManagementPermissions.All;
}
