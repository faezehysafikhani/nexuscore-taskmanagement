using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Application.Dtos;
using Nexus.Knowledge.Domain;
using Nexus.Knowledge.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Knowledge.Endpoints;

public static class KnowledgeDocumentEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/knowledge/documents").WithTags("Knowledge").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, string? search, KnowledgeDocumentType? documentType, IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.SearchAsync(currentUser.TenantId.Value, search, documentType, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(KnowledgePermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(KnowledgePermissions.View);

        group.MapGet("/{id:guid}/download", async (Guid id, IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DownloadAsync(id, cancellationToken);
                if (result.IsFailure)
                {
                    return result.ToApiResult();
                }

                var (content, fileName, contentType) = result.Value;
                return Results.File(content, contentType, fileName);
            })
            .RequireAuthorization(KnowledgePermissions.View);

        group.MapPost("/", async (
                IFormFile file, ICurrentUserContext currentUser, string title, string? description, KnowledgeDocumentType documentType,
                IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var request = new UploadKnowledgeDocumentRequest(currentUser.TenantId.Value, title, description, documentType, file.FileName, file.ContentType);
                await using var stream = file.OpenReadStream();
                return (await service.UploadAsync(request, stream, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(KnowledgePermissions.Upload);

        group.MapPut("/{id:guid}", async (Guid id, UpdateKnowledgeDocumentRequest request, IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(KnowledgePermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IKnowledgeDocumentService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(KnowledgePermissions.Delete);

        return app;
    }
}
