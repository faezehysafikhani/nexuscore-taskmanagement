using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Domain;
using Nexus.ProjectManagement.History.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.History.Endpoints;

public static class ProjectHistoryEndpoints
{
    public static IEndpointRouteBuilder MapProjectHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/history").WithTags("Project History").RequireAuthorization();

        group.MapGet("/", async (
                Guid projectId, string? entityName, ProjectChangeKind? kind, Guid? userId,
                DateTimeOffset? from, DateTimeOffset? to, int? skip, int? take,
                ICurrentUserContext currentUser, IProjectHistoryService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var query = new ProjectHistoryQuery(currentUser.TenantId.Value, projectId, entityName, kind, userId, from, to, skip ?? 0, take ?? 50);
                return (await service.QueryAsync(query, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectHistoryPermissions.View);

        return app;
    }
}
