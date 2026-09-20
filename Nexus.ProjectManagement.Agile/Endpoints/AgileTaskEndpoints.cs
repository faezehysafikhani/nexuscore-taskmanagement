using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Agile.Endpoints;

public static class AgileTaskEndpoints
{
    public static IEndpointRouteBuilder MapAgileTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/agile/tasks").WithTags("Agile Tasks").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, int? sprintNumber, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, sprintNumber, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.View);

        group.MapPost("/", async (CreateAgileTaskRequest request, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateAgileTaskRequest request, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        group.MapPut("/{id:guid}/status", async (Guid id, ChangeAgileTaskStatusRequest request, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IAgileTaskService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(AgilePermissions.Submit);

        group.MapPost("/generate", async (Guid projectId, string projectGoal, [FromServices] IAgileTaskGenerator? generator, CancellationToken cancellationToken) =>
            {
                if (generator is null)
                {
                    return Results.Problem("AI agile task generation is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                var suggestions = await generator.GenerateAsync(projectId, projectGoal, cancellationToken);
                return Results.Ok(suggestions);
            })
            .RequireAuthorization(AgilePermissions.Create);

        return app;
    }
}
