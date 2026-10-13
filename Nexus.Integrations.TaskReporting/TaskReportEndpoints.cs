using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Integrations.TaskReporting;

public static class TaskReportEndpoints
{
    /// <summary>
    /// GET /api/task-reports/scope - whose tasks the caller can report on, and the positions and
    /// tags to filter by. GET /api/task-reports/tasks - the report. Both need Tasks.View; what a
    /// report covers follows the organization structure (see TaskReportService).
    /// </summary>
    public static IEndpointRouteBuilder MapTaskReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/task-reports").WithTags("Task reports").RequireAuthorization(TaskManagementPermissions.View);

        group.MapGet("/scope", async (ICurrentUserContext user, ITaskReportService service, CancellationToken ct) =>
            user is { TenantId: { } tenant, UserId: { } me }
                ? (await service.GetScopeAsync(tenant, me, ct)).ToApiResult()
                : Results.Unauthorized());

        group.MapGet("/tasks", async (
                Guid? personId,
                string? personRole,
                Guid? positionId,
                string? tag,
                string? type,
                string? statuses,
                DateOnly? dueFrom,
                DateOnly? dueTo,
                ICurrentUserContext user,
                ITaskReportService service,
                CancellationToken ct) =>
            {
                if (user is not { TenantId: { } tenant, UserId: { } me })
                {
                    return Results.Unauthorized();
                }

                var query = new TaskReportQuery(
                    personId,
                    personRole,
                    positionId,
                    tag,
                    type,
                    (statuses ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    dueFrom,
                    dueTo);
                return (await service.GetReportAsync(tenant, me, query, ct)).ToApiResult();
            });

        return app;
    }
}
