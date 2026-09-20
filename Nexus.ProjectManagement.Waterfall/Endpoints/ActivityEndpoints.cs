using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapWaterfallEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/activities").WithTags("Waterfall Activities").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IActivityService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IActivityService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/", async (CreateActivityRequest request, IActivityService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateActivityRequest request, IActivityService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Edit);

        group.MapPut("/{id:guid}/progress", async (Guid id, UpdateActivityProgressRequest request, IActivityService service, CancellationToken cancellationToken) =>
                (await service.UpdateProgressAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IActivityService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IActivityService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Submit);

        group.MapPost("/generate-wbs", async (Guid projectId, string projectGoal, [FromServices] IWbsGenerator? generator, CancellationToken cancellationToken) =>
            {
                if (generator is null)
                {
                    return Results.Problem(
                        "AI WBS generation is not configured for this deployment.",
                        statusCode: StatusCodes.Status501NotImplemented);
                }

                var suggestions = await generator.GenerateAsync(projectId, projectGoal, cancellationToken);
                return Results.Ok(suggestions);
            })
            .RequireAuthorization(WaterfallPermissions.Create);

        return app;
    }
}
