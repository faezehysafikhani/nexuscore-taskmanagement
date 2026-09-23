using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using Nexus.TaskManagement.Realtime;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Endpoints;

public static class TaskManagementEndpoints
{
    /// <summary>
    /// Every TaskManagement route. One call from the host, matching how the other modules
    /// expose themselves.
    /// </summary>
    public static IEndpointRouteBuilder MapTaskManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapTaskEndpoints();
        app.MapSubTaskEndpoints();
        app.MapRepetitiveTaskEndpoints();
        app.MapTaskTagEndpoints();
        app.MapTaskFileEndpoints();
        app.MapNoteEndpoints();
        app.MapHub<TaskManagementHub>(TaskManagementHub.Route);
        return app;
    }

    private static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/tasks")
            .WithTags("Tasks")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/", async (
                ICurrentUserContext currentUser,
                int? pageNumber, int? pageSize, string? search,
                TaskItemStatus? status, TaskPriority? priority,
                bool? isProject, bool? isRecurring,
                Guid? assignedUserId, Guid? assignedUserGroupId, Guid? tagId,
                DateOnly? dueFrom, DateOnly? dueTo, bool? overdue,
                TaskSortBy? sortBy, bool? sortDescending,
                ITaskService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListTasksRequest(
                    currentUser.TenantId.Value, pageNumber ?? 1, pageSize ?? 20, search,
                    status, priority, isProject, isRecurring, assignedUserId, assignedUserGroupId,
                    tagId, dueFrom, dueTo, overdue,
                    sortBy ?? TaskSortBy.ModifiedAtUtc, sortDescending ?? true);

                return (await service.ListAsync(request, cancellationToken)).ToApiResult();
            })
            .WithSummary("List tasks, projects and recurring tasks")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Get one task with its subtasks, tags, files and schedule")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Create a task, a project with its subtasks, or a recurring task")
            .RequireAuthorization(TaskManagementPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Update a task")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete a task and everything under it")
            .RequireAuthorization(TaskManagementPermissions.Delete);

        group.MapPatch("/{id:guid}/status", async (Guid id, ChangeTaskStatusRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Change a task's status")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/priority", async (Guid id, ChangeTaskPriorityRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangePriorityAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Change a task's priority")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/assigned-user", async (Guid id, AssignUserRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AssignUserAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Assign a task to a user and collaborators")
            .RequireAuthorization(TaskManagementPermissions.Assign);

        group.MapPatch("/{id:guid}/assigned-user-group", async (Guid id, AssignUserGroupRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AssignUserGroupAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Assign a task to a team (UserGroup)")
            .RequireAuthorization(TaskManagementPermissions.Assign);

        // --- Subtasks of a task ---
        group.MapGet("/{id:guid}/subtasks", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetSubTasksAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's subtasks")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/subtasks", async (Guid id, CreateSubTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.CreateSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add a subtask")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        // --- Comments ---
        group.MapGet("/{id:guid}/comments", async (Guid id, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's comments")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/comments", async (Guid id, CreateTaskCommentRequest request, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add a comment")
            .RequireAuthorization(TaskManagementPermissions.Comment);

        // --- History, served from the shared AuditLog ---
        group.MapGet("/{id:guid}/activity", async (Guid id, ITaskActivityService service, CancellationToken cancellationToken) =>
                (await service.GetForTaskAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Task history, read from the shared audit log")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/activity", async (Guid id, CreateTaskActivityRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AddActivityEntryAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add an entry to the task history (recorded under the caller)")
            .RequireAuthorization(TaskManagementPermissions.View);

        // --- Tags and files on a task ---
        group.MapGet("/{id:guid}/files", async (Guid id, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.GetByTaskIdAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's attachments")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/tags", async (Guid id, AssignTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.AssignToTaskAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Attach a tag to a task")
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}/tags/{tagId:guid}", async (Guid id, Guid tagId, ITagService service, CancellationToken cancellationToken) =>
                (await service.RemoveFromTaskAsync(id, tagId, cancellationToken)).ToApiResult())
            .WithSummary("Detach a tag from a task")
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapSubTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/subtasks")
            .WithTags("Subtasks")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetSubTaskAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPut("/{id:guid}", async (Guid id, UpdateSubTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/status", async (Guid id, ChangeSubTaskStatusRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangeSubTaskStatusAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Complete or reopen a subtask")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteSubTaskAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete a subtask - refused for a project's last one")
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapGet("/{id:guid}/files", async (Guid id, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.GetBySubTaskIdAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/tags", async (Guid id, AssignTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.AssignToSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}/tags/{tagId:guid}", async (Guid id, Guid tagId, ITagService service, CancellationToken cancellationToken) =>
                (await service.RemoveFromSubTaskAsync(id, tagId, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapRepetitiveTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/repetitive-tasks")
            .WithTags("Recurring tasks")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/", async (
                ICurrentUserContext currentUser, int? pageNumber, int? pageSize, bool? isActive,
                IRepetitiveTaskService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListRepetitiveTasksRequest(
                    currentUser.TenantId.Value, pageNumber ?? 1, pageSize ?? 20, isActive);
                return (await service.ListAsync(request, cancellationToken)).ToApiResult();
            })
            .WithSummary("List recurrence schedules")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateRepetitiveTaskRequest request, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Attach a recurrence schedule to an existing task")
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPut("/{id:guid}", async (Guid id, UpdateRepetitiveTaskRequest request, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapDelete("/{id:guid}", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Remove a schedule; the task itself stays")
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPost("/{id:guid}/enable", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.SetActiveAsync(id, true, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPost("/{id:guid}/disable", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.SetActiveAsync(id, false, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/tags")
            .WithTags("Task tags")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/", async (string? search, ITagService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(search, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}", async (Guid id, ITagService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/files")
            .WithTags("Task files")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        // Multipart rather than JSON: the 200 KB ceiling is measured from the bytes that
        // actually arrive, and base64 in a JSON body would inflate them by a third.
        group.MapPost("/tasks/{taskId:guid}", async (
                Guid taskId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToTaskAsync(taskId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Upload an attachment to a task (max 200 KB)")
            .DisableAntiforgery()
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        group.MapPost("/subtasks/{subTaskId:guid}", async (
                Guid subTaskId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToSubTaskAsync(subTaskId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Upload an attachment to a subtask (max 200 KB)")
            .DisableAntiforgery()
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        group.MapPost("/comments/{commentId:guid}", async (
                Guid commentId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToCommentAsync(commentId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Attach a file to your own comment (max 200 KB)")
            .DisableAntiforgery()
            .RequireAuthorization(TaskManagementPermissions.Comment);

        group.MapGet("/{fileId:guid}/content", async (
                Guid fileId, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DownloadAsync(fileId, cancellationToken);
                if (result.IsFailure || result.Value is not { } download)
                {
                    return result.ToApiResult();
                }

                return Results.File(download.Content, download.ContentType, download.FileName);
            })
            .WithSummary("Download an attachment")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapDelete("/{linkId:guid}", async (Guid linkId, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(linkId, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        return app;
    }

    private static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/notes")
            .WithTags("Personal notes")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>();

        // Notes are private, so there is no "list another user's notes" route by design -
        // every route below resolves the owner from the caller's own token.
        group.MapGet("/", async (INoteService service, CancellationToken cancellationToken) =>
                (await service.GetMyNotesAsync(cancellationToken)).ToApiResult())
            .WithSummary("List the signed-in user's notes")
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapGet("/{id:guid}", async (Guid id, INoteService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapPost("/", async (CreateNoteRequest request, INoteService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapPut("/{id:guid}", async (Guid id, UpdateNoteRequest request, INoteService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapDelete("/{id:guid}", async (Guid id, INoteService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        // Comments live here too - they are edited by id, not through their task.
        var comments = app.MapGroup("/api/task-management/comments")
            .WithTags("Task comments")
            .RequireAuthorization()
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        comments.MapPut("/{id:guid}", async (Guid id, UpdateTaskCommentRequest request, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Edit your own comment")
            .RequireAuthorization(TaskManagementPermissions.Comment);

        comments.MapDelete("/{id:guid}", async (Guid id, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete your own comment")
            .RequireAuthorization(TaskManagementPermissions.Comment);

        return app;
    }

    private static async Task<UploadFileRequest> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return new UploadFileRequest(file.FileName, file.ContentType, stream.ToArray());
    }
}
