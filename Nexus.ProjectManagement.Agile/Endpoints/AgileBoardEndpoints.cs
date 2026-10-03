using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Agile.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Agile.Endpoints;

public static class AgileBoardEndpoints
{
    /// <summary>Mapped by <see cref="AgileTaskEndpoints.MapAgileTaskEndpoints"/>, so a host that
    /// already maps Agile picks these up without a new call.</summary>
    public static IEndpointRouteBuilder MapAgileBoardEndpoints(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/api/project-management/agile").WithTags("Agile Board").RequireAuthorization();

        root.MapGet("/board", async (
                Guid projectId, int? sprintNumber, Guid? responsibleUserId, AgileTaskPriority? priority,
                IAgileBoardService service, CancellationToken cancellationToken) =>
                (await service.GetBoardAsync(projectId, sprintNumber, responsibleUserId, priority, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        root.MapGet("/backlog", async (Guid projectId, IAgileBoardService service, CancellationToken cancellationToken) =>
                (await service.GetBacklogAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        var tasks = root.MapGroup("/tasks");

        tasks.MapPost("/{id:guid}/move", async (Guid id, MoveAgileTaskRequest request, IAgileBoardService service, CancellationToken cancellationToken) =>
                (await service.MoveAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        tasks.MapPut("/{id:guid}/estimate", async (Guid id, SetStoryPointsRequest request, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.SetStoryPointsAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        tasks.MapGet("/{id:guid}/checklist", async (Guid id, IAgileChecklistService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        tasks.MapPost("/{id:guid}/checklist", async (Guid id, CreateChecklistItemRequest request, IAgileChecklistService service, CancellationToken cancellationToken) =>
                (await service.AddAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        tasks.MapPut("/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, UpdateChecklistItemRequest request, IAgileChecklistService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, itemId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        tasks.MapDelete("/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, IAgileChecklistService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, itemId, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        var sprints = root.MapGroup("/sprints");

        sprints.MapGet("/", async (Guid projectId, ISprintService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        sprints.MapGet("/{id:guid}", async (Guid id, ISprintService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        sprints.MapPost("/", async (CreateSprintRequest request, ISprintService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapPut("/{id:guid}", async (Guid id, UpdateSprintRequest request, ISprintService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapPost("/{id:guid}/start", async (Guid id, StartSprintRequest request, ISprintService service, CancellationToken cancellationToken) =>
                (await service.StartAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapPost("/{id:guid}/complete", async (Guid id, CompleteSprintRequest request, ISprintService service, CancellationToken cancellationToken) =>
                (await service.CompleteAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapDelete("/{id:guid}", async (Guid id, ISprintService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapPost("/{id:guid}/tasks", async (Guid id, AssignSprintTasksRequest request, ISprintService service, CancellationToken cancellationToken) =>
                (await service.AssignTasksAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        sprints.MapDelete("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, ISprintService service, CancellationToken cancellationToken) =>
                (await service.RemoveTaskAsync(id, taskId, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.ManageSprints);

        return app;
    }
}
