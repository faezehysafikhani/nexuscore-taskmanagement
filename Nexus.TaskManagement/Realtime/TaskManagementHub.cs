using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Nexus.TaskManagement.Application.Dtos;
using NexusCore.Application.Identity.Services;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Realtime;

/// <summary>
/// Live updates for task screens. A connection joins its tenant's group; after any successful
/// change to tasks, subtasks, comments, files, tags or schedules the tenant receives
/// "TasksChanged" and re-reads. The message carries no task data - at most the id of the task
/// that changed (TaskId, null when it is not known) - and clients re-read through the normal,
/// permission-checked endpoints (a task the reader may not see answers "not found"), so the hub
/// can never leak something the reader may not see.
///
/// Each connection also counts toward its user's presence (online while at least one of their
/// connections is open), since the app keeps this connection open for as long as it is signed in.
/// </summary>
[Authorize]
public sealed class TaskManagementHub(ICurrentUserContext currentUser, IUserPresenceTracker presence) : Hub
{
    public const string Route = "/hubs/task-management";
    public const string TasksChangedEvent = "TasksChanged";

    public static string TenantGroup(Guid tenantId) => $"task-management:tenant:{tenantId:N}";

    public override async Task OnConnectedAsync()
    {
        if (currentUser.TenantId is { } tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        }

        if (currentUser.UserId is { } userId)
        {
            Context.Items[PresenceUserKey] = userId;
            presence.Connected(userId, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(PresenceUserKey, out var value) && value is Guid userId)
        {
            presence.Disconnected(userId, Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    private const string PresenceUserKey = "presence-user";
}

/// <summary>
/// Marks a POST that only reads (a batch query): no "TasksChanged" after it. Without this mark every client
/// would reload on each read, and the reloads would announce again.
/// </summary>
public sealed class ReadOnlyTaskEndpoint
{
    public static readonly ReadOnlyTaskEndpoint Instance = new();
}


/// <param name="TaskId">The task that changed, when the change names one: clients then re-read just that task.</param>
public sealed record TasksChangedMessage(string Resource, string Method, DateTimeOffset AtUtc, Guid? TaskId = null);

/// <summary>
/// Runs after each task-management write. On a 2xx answer it tells the caller's tenant that
/// something changed. A failed broadcast never fails the request that already succeeded.
/// </summary>
public sealed class TaskChangeBroadcastFilter(
    IHubContext<TaskManagementHub> hub,
    ICurrentUserContext currentUser,
    Microsoft.Extensions.Logging.ILogger<TaskChangeBroadcastFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ReadOnlyTaskEndpoint>() is not null
            || currentUser.TenantId is not { } tenantId)
        {
            return result;
        }

        var status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : context.HttpContext.Response.StatusCode;
        if (status is < 200 or >= 300)
        {
            return result;
        }

        try
        {
            await hub.Clients.Group(TaskManagementHub.TenantGroup(tenantId)).SendAsync(
                TaskManagementHub.TasksChangedEvent,
                // "Something changed", and at most which task: its id says nothing about it, and the whole
                // organization receives this - including people who may not open that task. Clients re-read
                // what they are allowed to see (a task they may not see answers "not found").
                new TasksChangedMessage(string.Empty, string.Empty, DateTimeOffset.UtcNow, TaskIdOf(context.HttpContext, result)),
                context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "Could not broadcast a task change.");
        }

        return result;
    }

    /// <summary>
    /// The task a write is about: from the route (/tasks/{id}/..., /files/tasks/{taskId}) or from what the endpoint
    /// returned (a created task, a subtask, a comment). Null when the change does not name one - clients then reload all.
    /// </summary>
    internal static Guid? TaskIdOf(HttpContext http, object? result)
    {
        var path = http.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/task-management/tasks/", StringComparison.OrdinalIgnoreCase)
            && http.Request.RouteValues.TryGetValue("id", out var id)
            && Guid.TryParse(id?.ToString(), out var routeTaskId))
        {
            return routeTaskId;
        }

        if (path.StartsWith("/api/task-management/files/tasks/", StringComparison.OrdinalIgnoreCase)
            && http.Request.RouteValues.TryGetValue("taskId", out var fileTaskId)
            && Guid.TryParse(fileTaskId?.ToString(), out var taskIdOfFile))
        {
            return taskIdOfFile;
        }

        return result is IValueHttpResult { Value: { } value }
            ? value switch
            {
                TaskDto task => (Guid?)task.Id,
                SubTaskDto subTask => (Guid?)subTask.TaskId,
                TaskCommentDto comment => (Guid?)comment.TaskId,
                _ => (Guid?)null
            }
            : null;
    }
}
