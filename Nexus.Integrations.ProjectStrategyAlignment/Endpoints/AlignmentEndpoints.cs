using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using Nexus.Integrations.StrategyAlignment.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Integrations.StrategyAlignment.Endpoints;

public static class AlignmentEndpoints
{
    public static IEndpointRouteBuilder MapProjectStrategyAlignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations/project-strategy-alignment").WithTags("Project-Strategy Alignment").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, Guid? projectId, Guid? strategyId, IAlignmentService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, projectId, strategyId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(AlignmentPermissions.View);

        group.MapPost("/", async (CreateAlignmentRequest request, IAlignmentService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AlignmentPermissions.Manage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateAlignmentRequest request, IAlignmentService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(AlignmentPermissions.Manage);

        return app;
    }
}
