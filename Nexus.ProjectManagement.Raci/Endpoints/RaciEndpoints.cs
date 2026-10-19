using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Raci.Application;
using Nexus.ProjectManagement.Raci.Application.Dtos;
using Nexus.ProjectManagement.Raci.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Raci.Endpoints;

public static class RaciEndpoints
{
    public static IEndpointRouteBuilder MapRaciEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/raci").WithTags("RACI").RequireAuthorization();

        // Every role of a project's matrix, as flat rows (item, person, role).
        group.MapGet("/", async (ICurrentUserContext user, Guid projectId, IRaciService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.ListByProjectAsync(tenant, projectId, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(RaciPermissions.View);

        // Replaces the roles of one item; the answer is the project's whole matrix.
        group.MapPut("/items", async (ICurrentUserContext user, SetItemRaciRequest request, IRaciService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.SetItemAsync(tenant, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(RaciPermissions.Edit);

        return app;
    }
}
