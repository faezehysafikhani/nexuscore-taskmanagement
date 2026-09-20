using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Progress.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/progress-updates").WithTags("Progress").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IProgressService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProgressPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IProgressService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProgressPermissions.View);

        group.MapPost("/", async (CreateProgressUpdateRequest request, IProgressService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProgressPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateProgressUpdateRequest request, IProgressService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProgressPermissions.Edit);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IProgressService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProgressPermissions.Submit);

        group.MapGet("/executive-summary", async (Guid projectId, [FromServices] IExecutiveSummaryGenerator? generator, CancellationToken cancellationToken) =>
            {
                if (generator is null)
                {
                    return Results.Problem("AI executive summary generation is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                var summary = await generator.GenerateAsync(projectId, cancellationToken);
                return Results.Ok(summary);
            })
            .RequireAuthorization(ProgressPermissions.View);

        return app;
    }
}
