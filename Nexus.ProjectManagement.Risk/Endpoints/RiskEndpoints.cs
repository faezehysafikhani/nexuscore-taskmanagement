using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.RiskManagement.Endpoints;

public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/risks").WithTags("Risks").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IRiskService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IRiskService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapPost("/", async (CreateRiskRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateRiskRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Edit);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IRiskService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Submit);

        group.MapPost("/analyze", async (Guid projectId, string projectContext, [FromServices] IRiskAnalyzer? analyzer, CancellationToken cancellationToken) =>
            {
                if (analyzer is null)
                {
                    return Results.Problem("AI risk analysis is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                var suggestions = await analyzer.AnalyzeAsync(projectId, projectContext, cancellationToken);
                return Results.Ok(suggestions);
            })
            .RequireAuthorization(RiskPermissions.Create);

        return app;
    }
}
