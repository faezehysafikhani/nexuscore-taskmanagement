using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Domain;
using Nexus.ProjectManagement.Documents.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Documents.Endpoints;

public static class ProjectDocumentEndpoints
{
    public static IEndpointRouteBuilder MapProjectDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/documents").WithTags("Project Documents").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IProjectDocumentService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectDocumentPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IProjectDocumentService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectDocumentPermissions.View);

        group.MapGet("/{id:guid}/download", async (Guid id, IProjectDocumentService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DownloadAsync(id, cancellationToken);
                if (result.IsFailure)
                {
                    return result.ToApiResult();
                }

                var (content, fileName, contentType) = result.Value;
                return Results.File(content, contentType, fileName);
            })
            .RequireAuthorization(ProjectDocumentPermissions.View);

        group.MapPost("/", async (
                IFormFile file, ICurrentUserContext currentUser, Guid projectId, string description, ProjectDocumentType documentType,
                IProjectDocumentService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var request = new UploadProjectDocumentRequest(currentUser.TenantId.Value, projectId, description, documentType, file.FileName, file.ContentType);
                await using var stream = file.OpenReadStream();
                return (await service.UploadAsync(request, stream, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectDocumentPermissions.Upload);

        group.MapPut("/{id:guid}", async (Guid id, UpdateProjectDocumentRequest request, IProjectDocumentService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectDocumentPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IProjectDocumentService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectDocumentPermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IProjectDocumentService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectDocumentPermissions.Submit);

        group.MapGet("/{id:guid}/summary", async (Guid id, [FromServices] IDocumentSummaryGenerator? generator, CancellationToken cancellationToken) =>
            {
                if (generator is null)
                {
                    return Results.Problem("AI document summary is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                return Results.Ok(await generator.SummarizeAsync(id, cancellationToken));
            })
            .RequireAuthorization(ProjectDocumentPermissions.View);

        group.MapGet("/{id:guid}/relevance", async (Guid id, Guid projectId, [FromServices] IDocumentRelevanceAnalyzer? analyzer, CancellationToken cancellationToken) =>
            {
                if (analyzer is null)
                {
                    return Results.Problem("AI document relevance analysis is not configured for this deployment.", statusCode: StatusCodes.Status501NotImplemented);
                }

                return Results.Ok(await analyzer.AnalyzeRelevanceAsync(id, projectId, cancellationToken));
            })
            .RequireAuthorization(ProjectDocumentPermissions.View);

        return app;
    }
}
