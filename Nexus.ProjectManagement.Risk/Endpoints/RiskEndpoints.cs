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

        group.MapPost("/for-projects", async (ListRisksForProjectsRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.ListForProjectsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapGet("/settings", async (Guid projectId, IRiskService service, CancellationToken cancellationToken) =>
                (await service.GetSettingsAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapPost("/settings/for-projects", async (RiskSettingsForProjectsRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.GetSettingsForProjectsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapPut("/settings", async (SaveRiskSettingsRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.SaveSettingsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Edit);

        group.MapPut("/{id:guid}/response-status", async (Guid id, ChangeRiskResponseStatusRequest request, IRiskService service, CancellationToken cancellationToken) =>
                (await service.ChangeResponseStatusAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Edit);

        group.MapPost("/response-submissions/for-projects", async (ListRisksForProjectsRequest request, IRiskResponseSubmissionService service, CancellationToken cancellationToken) =>
                (await service.ListForProjectsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapGet("/response-submissions", async (Guid riskId, IRiskResponseSubmissionService service, CancellationToken cancellationToken) =>
                (await service.ListByRiskAsync(riskId, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.View);

        group.MapPost("/response-submissions", async (SubmitRiskResponseRequest request, IRiskResponseSubmissionService service, CancellationToken cancellationToken) =>
                (await service.SubmitAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.SubmitResponse);

        group.MapGet("/matrix", async (Guid projectId, IRiskService service, CancellationToken cancellationToken) =>
                (await service.GetMatrixAsync(projectId, cancellationToken)).ToApiResult())
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

        group.MapDelete("/{id:guid}", async (Guid id, IRiskService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(RiskPermissions.Delete);

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
