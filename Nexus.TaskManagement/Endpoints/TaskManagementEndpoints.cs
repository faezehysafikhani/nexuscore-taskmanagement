using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using Nexus.TaskManagement.Realtime;
using NexusCore.Application.Common;
using NexusCore.Application.Files;
using NexusCore.Application.Security.RateLimiting;
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
        app.MapTaskStatusEndpoints();
        app.MapTaskRequestEndpoints();
        app.MapTaskPlanningEndpoints();
        app.MapTaskFileEndpoints();
        app.MapNoteEndpoints();
        app.MapReminderSettingsEndpoints();
        app.MapAssignmentLimitEndpoint();
        app.MapHub<TaskManagementHub>(TaskManagementHub.Route, options => options.CloseOnAuthenticationExpiration = true)
            .RequireRateLimiting(NexusRateLimitPolicies.Realtime);
        return app;
    }

    /// <summary>
    /// When tasks are reminded of (SMS panel). Anyone signed in may read it - the task screen also
    /// learns from it that notifications are kept on the server; changing it is for those who
    /// may change the SMS panel.
    /// </summary>
    private static IEndpointRouteBuilder MapReminderSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/reminder-settings")
            .WithTags("Tasks - reminders")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        group.MapGet("/", async (ITaskReminderSettingsService service, CancellationToken cancellationToken) =>
            (await service.GetAsync(cancellationToken)).ToApiResult());

        group.MapPut("/", async (TaskReminderSettingsDto request, ITaskReminderSettingsService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(NexusCore.Application.Identity.Permissions.IdentityPermissions.SmsSettingsUpdate);

        return app;
    }

    /// <summary>
    /// Whom the signed-in user may not make responsible for a task (where the product limits it -
    /// see IWorkAssignmentScope), so the task screen offers only the others. The server checks again.
    /// </summary>
    private static IEndpointRouteBuilder MapAssignmentLimitEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/task-management/assignment-limit", async (
                ICurrentUserContext user, NexusCore.Application.Identity.Interfaces.IWorkAssignmentScope scope,
                NexusCore.Application.Identity.Interfaces.IUserReachScope reachScope, CancellationToken cancellationToken) =>
            {
                if (user.TenantId is not { } tenantId || user.UserId is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var limit = await scope.GetLimitAsync(tenantId, userId, cancellationToken);
                // Kept to one's own company: only its people (AllowedUserIds; null when not limited).
                var reach = await reachScope.GetReachAsync(tenantId, userId, cancellationToken);
                return Results.Ok(new AssignmentLimitDto(
                    limit is not null,
                    [.. (limit?.BlockedUserIds ?? new HashSet<Guid>()).Union(reach?.PlacedElsewhere ?? new HashSet<Guid>())],
                    reach?.UserIds.ToList()));
            })
            .WithTags("Tasks")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        // Where tasks are given to posts: the posts to choose from (Enabled false: give them to people).
        app.MapGet("/api/task-management/assignable-posts", async (ITaskService service, CancellationToken cancellationToken) =>
                await service.ListAssignablePostsAsync(cancellationToken) is { IsSuccess: true } result
                    ? Results.Ok(result.Value)
                    : Results.Unauthorized())
            .WithTags("Tasks")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/tasks")
            .WithTags("Tasks")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/", async (
                ICurrentUserContext currentUser,
                int? pageNumber, int? pageSize, string? search,
                TaskItemStatus? status, TaskPriority? priority,
                bool? isProject, bool? isRecurring,
                Guid? assignedUserId, Guid? assignedUserGroupId, Guid? tagId, Guid? customStatusId,
                DateOnly? dueFrom, DateOnly? dueTo, bool? overdue,
                TaskSortBy? sortBy, bool? sortDescending, bool? archived,
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
                    sortBy ?? TaskSortBy.ModifiedAtUtc, sortDescending ?? true, archived ?? false, customStatusId);

                return (await service.ListAsync(request, cancellationToken)).ToApiResult();
            })
            .WithSummary("List tasks, projects and recurring tasks (archived=true lists the archive instead)")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Get one task with its subtasks, tags, files and schedule")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Create a task, a project with its subtasks, or a recurring task")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Create);

        group.MapPost("/quick", async (QuickCreateTaskRequest request, ITaskBatchService service, CancellationToken cancellationToken) =>
                (await service.QuickCreateAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Quick add: a title is enough (due today unless a date is given, given to the caller)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Create);

        group.MapPost("/from-message", async (TaskFromMessageRequest request, ITaskBatchService service, CancellationToken cancellationToken) =>
                (await service.FromMessageAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Message to task: a chat message becomes a task (the message is kept as its source)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Create);

        group.MapPost("/bulk", async (BulkTaskRequest request, ITaskBatchService service, CancellationToken cancellationToken) =>
                (await service.BulkAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("One action (status, custom status, priority, archive, restore) over up to 100 tasks; each task answers for itself")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPost("/{id:guid}/submit-review", async (Guid id, SubmitTaskReviewRequest request, ITaskReviewService service, CancellationToken cancellationToken) =>
                (await service.SubmitAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Submit the task for review; the approval chain decides (none set up: it is completed at once)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPost("/in-review", async (TaskReviewItemsRequest request, ITaskReviewService service, CancellationToken cancellationToken) =>
                (await service.ListInReviewAsync(request.Ids, cancellationToken)).ToApiResult())
            .WithSummary("Title, due date and submitter of the tasks (among the ids) that are waiting for review - for the approver's list")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Update a task")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPost("/{id:guid}/archive", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ArchiveAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Put a task or project in the archive: hidden everywhere, nothing is deleted")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPost("/{id:guid}/restore", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.RestoreAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Bring a task or project back from the archive")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete a task and everything under it")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Delete);

        group.MapPatch("/{id:guid}/status", async (Guid id, ChangeTaskStatusRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Change a task's status")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/priority", async (Guid id, ChangeTaskPriorityRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangePriorityAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Change a task's priority")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/assigned-user", async (Guid id, AssignUserRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AssignUserAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Assign a task to a user and collaborators")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Assign);

        group.MapPatch("/{id:guid}/assigned-user-group", async (Guid id, AssignUserGroupRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AssignUserGroupAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Assign a task to a team (UserGroup)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Assign);

        // --- Subtasks of a task ---
        group.MapGet("/{id:guid}/subtasks", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetSubTasksAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's subtasks")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/subtasks", async (Guid id, CreateSubTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.CreateSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add a subtask")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        // --- Comments ---
        group.MapGet("/{id:guid}/comments", async (Guid id, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's comments")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/comments", async (Guid id, CreateTaskCommentRequest request, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add a comment")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Comment);

        // --- History, served from the shared AuditLog ---
        group.MapGet("/{id:guid}/activity", async (Guid id, ITaskActivityService service, CancellationToken cancellationToken) =>
                (await service.GetForTaskAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Task history, read from the shared audit log")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/activity", async (Guid id, CreateTaskActivityRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.AddActivityEntryAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Add an entry to the task history (recorded under the caller)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.View);

        // --- Tags and files on a task ---
        group.MapGet("/{id:guid}/files", async (Guid id, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.GetByTaskIdAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("List a task's attachments")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/tags", async (Guid id, AssignTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.AssignToTaskAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Attach a tag to a task")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}/tags/{tagId:guid}", async (Guid id, Guid tagId, ITagService service, CancellationToken cancellationToken) =>
                (await service.RemoveFromTaskAsync(id, tagId, cancellationToken)).ToApiResult())
            .WithSummary("Detach a tag from a task")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapSubTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/subtasks")
            .WithTags("Subtasks")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.GetSubTaskAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPut("/{id:guid}", async (Guid id, UpdateSubTaskRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapPatch("/{id:guid}/status", async (Guid id, ChangeSubTaskStatusRequest request, ITaskService service, CancellationToken cancellationToken) =>
                (await service.ChangeSubTaskStatusAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Complete or reopen a subtask")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteSubTaskAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete a subtask - refused for a project's last one")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Edit);

        group.MapGet("/{id:guid}/files", async (Guid id, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.GetBySubTaskIdAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/{id:guid}/tags", async (Guid id, AssignTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.AssignToSubTaskAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}/tags/{tagId:guid}", async (Guid id, Guid tagId, ITagService service, CancellationToken cancellationToken) =>
                (await service.RemoveFromSubTaskAsync(id, tagId, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapRepetitiveTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/repetitive-tasks")
            .WithTags("Recurring tasks")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
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
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPut("/{id:guid}", async (Guid id, UpdateRepetitiveTaskRequest request, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapDelete("/{id:guid}", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Remove a schedule; the task itself stays")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPost("/{id:guid}/enable", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.SetActiveAsync(id, true, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        group.MapPost("/{id:guid}/disable", async (Guid id, IRepetitiveTaskService service, CancellationToken cancellationToken) =>
                (await service.SetActiveAsync(id, false, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRecurring);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/tags")
            .WithTags("Task tags")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        group.MapGet("/", async (string? search, ITagService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(search, cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTagRequest request, ITagService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        group.MapDelete("/{id:guid}", async (Guid id, ITagService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageTags);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/task-management/tasks")
            .WithTags("Tasks - planning")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        tasks.MapGet("/{id:guid}/planning", async (Guid id, ITaskPlanningService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToApiResult())
            .WithSummary("What stands between the task and done: dependencies, blocker, acceptance criteria")
            .RequireAuthorization(TaskManagementPermissions.View);
        tasks.MapPost("/{id:guid}/dependencies", async (Guid id, AddTaskDependencyRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.AddDependencyAsync(id, r, ct)).ToApiResult())
            .WithSummary("The task cannot be completed before the given task is")
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapDelete("/{id:guid}/dependencies/{blockedByTaskId:guid}", async (Guid id, Guid blockedByTaskId, ITaskPlanningService s, CancellationToken ct) => (await s.RemoveDependencyAsync(id, blockedByTaskId, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPut("/{id:guid}/blocker", async (Guid id, SetTaskBlockerRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.SetBlockerAsync(id, r, ct)).ToApiResult())
            .WithSummary("Say what blocks the task (an empty reason clears it)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPost("/{id:guid}/criteria", async (Guid id, SaveCriterionRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.AddCriterionAsync(id, r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPut("/{id:guid}/criteria/{criterionId:guid}", async (Guid id, Guid criterionId, SaveCriterionRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.UpdateCriterionAsync(id, criterionId, r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPut("/{id:guid}/criteria/{criterionId:guid}/met", async (Guid id, Guid criterionId, SetCriterionMetRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.SetCriterionMetAsync(id, criterionId, r, ct)).ToApiResult())
            .WithSummary("Tick or untick an acceptance criterion; the task cannot be completed while one is not met")
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapDelete("/{id:guid}/criteria/{criterionId:guid}", async (Guid id, Guid criterionId, ITaskPlanningService s, CancellationToken ct) => (await s.DeleteCriterionAsync(id, criterionId, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPost("/{id:guid}/subtasks/bulk", async (Guid id, BulkSubTasksRequest r, ITaskPlanningService s, CancellationToken ct) => (await s.AddSubTasksAsync(id, r, ct)).ToApiResult())
            .WithSummary("Add many subtasks at once (one title per line in the UI)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Edit);
        tasks.MapPost("/{id:guid}/save-as-template", async (Guid id, SaveTaskAsTemplateRequest r, ITaskTemplateService s, CancellationToken ct) => (await s.SaveFromTaskAsync(id, r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.ManageTemplates);

        var wip = app.MapGroup("/api/task-management/wip").WithTags("Tasks - planning").RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi).AddEndpointFilter<RequestValidationFilter>();
        wip.MapGet("/", async (ITaskPlanningService s, CancellationToken ct) => (await s.GetWipAsync(ct)).ToApiResult())
            .WithSummary("The limit of tasks in progress per person (empty: no limit)")
            .RequireAuthorization(TaskManagementPermissions.View);
        wip.MapPut("/", async (WipSettingDto r, ITaskPlanningService s, CancellationToken ct) => (await s.SetWipAsync(r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.ManageStatuses);

        var templates = app.MapGroup("/api/task-management/templates").WithTags("Task templates").RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi).AddEndpointFilter<RequestValidationFilter>();
        templates.MapGet("/", async (ITaskTemplateService s, CancellationToken ct) => (await s.ListAsync(ct)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.View);
        templates.MapPost("/", async (SaveTaskTemplateRequest r, ITaskTemplateService s, CancellationToken ct) => (await s.CreateAsync(r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.ManageTemplates);
        templates.MapPut("/{id:guid}", async (Guid id, SaveTaskTemplateRequest r, ITaskTemplateService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.ManageTemplates);
        templates.MapDelete("/{id:guid}", async (Guid id, ITaskTemplateService s, CancellationToken ct) => (await s.DeleteAsync(id, ct)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.ManageTemplates);
        templates.MapPost("/{id:guid}/instantiate", async (Guid id, InstantiateTemplateRequest r, ITaskTemplateService s, CancellationToken ct) => (await s.InstantiateAsync(id, r, ct)).ToApiResult())
            .WithSummary("Make a task or project from the template")
            .RequireRateLimiting(NexusRateLimitPolicies.Write).RequireAuthorization(TaskManagementPermissions.Create);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/requests")
            .WithTags("Task requests")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<RequestValidationFilter>();

        group.MapGet("/categories", async (ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.ListCategoriesAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(TaskManagementPermissions.SubmitRequests);

        group.MapPost("/categories", async (SaveRequestCategoryRequest request, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.CreateCategoryAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRequests);

        group.MapPut("/categories/{id:guid}", async (Guid id, SaveRequestCategoryRequest request, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.UpdateCategoryAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageRequests);

        group.MapGet("/", async (bool? mine, TaskRequestStatus? status, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(mine ?? true, status, cancellationToken)).ToApiResult())
            .WithSummary("mine=true (default): my requests. mine=false: every request (request handlers)")
            .RequireAuthorization(TaskManagementPermissions.SubmitRequests);

        group.MapPost("/", async (SubmitTaskRequestRequest request, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.SubmitAsync(request, cancellationToken)).ToApiResult())
            .WithSummary("Submit a request; it is routed by its category and urgency")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.SubmitRequests);

        group.MapPost("/{id:guid}/convert", async (Guid id, ConvertTaskRequestRequest request, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.ConvertAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Turn the request into a task (the task keeps the request as its source)")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.HandleRequests);

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectTaskRequestRequest request, ITaskRequestService service, CancellationToken cancellationToken) =>
                (await service.RejectAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.HandleRequests);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/statuses")
            .WithTags("Task statuses")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<RequestValidationFilter>();

        group.MapGet("/", async (ITaskStatusService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(cancellationToken)).ToApiResult())
            .WithSummary("The organization's custom task statuses (each counts as one of the four built-in statuses)")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapPost("/", async (CreateTaskStatusRequest request, ITaskStatusService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageStatuses);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskStatusRequest request, ITaskStatusService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageStatuses);

        group.MapDelete("/{id:guid}", async (Guid id, ITaskStatusService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete a status; its tasks keep their built-in status")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageStatuses);

        return app;
    }

    private static IEndpointRouteBuilder MapTaskFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/files")
            .WithTags("Task files")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        // Multipart rather than JSON: the 200 KB ceiling is measured from the bytes that
        // actually arrive, and base64 in a JSON body would inflate them by a third.
        group.MapPost("/tasks/{taskId:guid}", async (
                Guid taskId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                if (TooLarge(file)) return PayloadTooLarge();
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToTaskAsync(taskId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Upload an attachment to a task (max 200 KB)")
            .DisableAntiforgery()
            .WithUploadSizeLimit()
            .RequireRateLimiting(NexusRateLimitPolicies.Upload)
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        group.MapPost("/subtasks/{subTaskId:guid}", async (
                Guid subTaskId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                if (TooLarge(file)) return PayloadTooLarge();
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToSubTaskAsync(subTaskId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Upload an attachment to a subtask (max 200 KB)")
            .DisableAntiforgery()
            .WithUploadSizeLimit()
            .RequireRateLimiting(NexusRateLimitPolicies.Upload)
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        group.MapPost("/comments/{commentId:guid}", async (
                Guid commentId, IFormFile file, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                if (TooLarge(file)) return PayloadTooLarge();
                var upload = await ReadAsync(file, cancellationToken);
                return (await service.UploadToCommentAsync(commentId, upload, cancellationToken)).ToApiResult();
            })
            .WithSummary("Attach a file to your own comment (max 200 KB)")
            .DisableAntiforgery()
            .WithUploadSizeLimit()
            .RequireRateLimiting(NexusRateLimitPolicies.Upload)
            .RequireAuthorization(TaskManagementPermissions.Comment);

        group.MapGet("/{fileId:guid}/content", async (
                Guid fileId, ITaskFileService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DownloadAsync(fileId, cancellationToken);
                if (result.IsFailure || result.Value is not { } download)
                {
                    return result.ToApiResult();
                }

                // The type comes from the extension, so a file stored earlier with a type the client chose is served safely too.
                return Results.File(download.Content, NexusCore.Application.Files.AllowedUploadTypes.ContentTypeFor(download.FileName), download.FileName);
            })
            .WithSummary("Download an attachment")
            .RequireAuthorization(TaskManagementPermissions.View);

        group.MapDelete("/{linkId:guid}", async (Guid linkId, ITaskFileService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(linkId, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.UploadFiles);

        return app;
    }

    private static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-management/notes")
            .WithTags("Personal notes")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
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
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapPut("/{id:guid}", async (Guid id, UpdateNoteRequest request, INoteService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        group.MapDelete("/{id:guid}", async (Guid id, INoteService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.ManageNotes);

        // Comments live here too - they are edited by id, not through their task.
        var comments = app.MapGroup("/api/task-management/comments")
            .WithTags("Task comments")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .AddEndpointFilter<TaskAccessScopeFilter>()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<TaskChangeBroadcastFilter>();

        comments.MapPut("/{id:guid}", async (Guid id, UpdateTaskCommentRequest request, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .WithSummary("Edit your own comment")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Comment);

        comments.MapDelete("/{id:guid}", async (Guid id, ITaskCommentService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .WithSummary("Delete your own comment")
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(TaskManagementPermissions.Comment);

        return app;
    }

    /// <summary>Said before a single byte of the file is read into memory.</summary>
    private static bool TooLarge(IFormFile file) => file.Length > TaskFileAsset.MaxFileSizeBytes;

    private static IResult PayloadTooLarge() =>
        Results.Problem(title: "payload_too_large", detail: "The file is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);

    private static async Task<UploadFileRequest> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return new UploadFileRequest(file.FileName, file.ContentType, stream.ToArray());
    }
}
