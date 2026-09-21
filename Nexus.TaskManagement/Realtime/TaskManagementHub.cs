using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Realtime;

/// <summary>
/// Live updates for task screens. A connection joins its tenant's group; after any successful
/// change to tasks, subtasks, comments, files, tags or schedules the tenant receives
/// "TasksChanged" and reloads. The message carries no task data - clients re-read through the
/// normal, permission-checked endpoints, so the hub can never leak something the reader may
/// not see.
/// </summary>
[Authorize]
public sealed class TaskManagementHub(ICurrentUserContext currentUser) : Hub
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

        await base.OnConnectedAsync();
    }
}

public sealed record TasksChangedMessage(string Resource, string Method, DateTimeOffset AtUtc);

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
        if (HttpMethods.IsGet(request.Method) || currentUser.TenantId is not { } tenantId)
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
                new TasksChangedMessage(request.Path.Value ?? string.Empty, request.Method, DateTimeOffset.UtcNow),
                context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "Could not broadcast a task change.");
        }

        return result;
    }
}
