using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using Nexus.ProjectManagement.StakeholderManagement.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.StakeholderManagement.Endpoints;

public static class StakeholderEndpoints
{
    public static IEndpointRouteBuilder MapStakeholderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/stakeholders").WithTags("Stakeholders").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IStakeholderService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(StakeholderPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IStakeholderService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(StakeholderPermissions.View);

        group.MapPost("/", async (CreateStakeholderRequest request, IStakeholderService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(StakeholderPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateStakeholderRequest request, IStakeholderService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(StakeholderPermissions.Edit);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IStakeholderService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(StakeholderPermissions.Submit);

        group.MapPost("/analyze", async (Guid projectId, string projectContext, [FromServices] IStakeholderAnalyzer? analyzer, CancellationToken cancellationToken) =>
            {
                if (analyzer is null)
                {
                    return Results.Problem("AI stakeholder analysis is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                var suggestions = await analyzer.AnalyzeAsync(projectId, projectContext, cancellationToken);
                return Results.Ok(suggestions);
            })
            .RequireAuthorization(StakeholderPermissions.Create);

        return app;
    }
}
