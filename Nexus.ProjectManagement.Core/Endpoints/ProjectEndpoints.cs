using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Core.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/projects").WithTags("Projects").RequireAuthorization();

        group.MapGet("/", async (
                ICurrentUserContext currentUser, int? pageNumber, int? pageSize, string? search,
                ProjectType? type, ProjectStatus? status, Guid? organizationUnitId, Guid? managerUserId,
                ProjectSortBy? sortBy, bool? sortDescending,
                IProjectService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListProjectsRequest(
                    currentUser.TenantId.Value, pageNumber ?? 1, pageSize ?? 20, search, type, status,
                    organizationUnitId, managerUserId, sortBy ?? ProjectSortBy.CreatedAtUtc, sortDescending ?? true);
                return (await service.ListAsync(request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.View);

        group.MapPost("/", async (CreateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.Edit);

        group.MapPost("/{id:guid}/archive", async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
                (await service.ArchiveAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.Submit);

        return app;
    }
}
