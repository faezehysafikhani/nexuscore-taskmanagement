using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.StrategyManagement.Endpoints;

public static class StrategyEndpoints
{
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/strategy").WithTags("Strategy").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, IStrategyService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(StrategyPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IStrategyService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(StrategyPermissions.View);

        group.MapPost("/", async (CreateStrategyRequest request, IStrategyService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(StrategyPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateStrategyRequest request, IStrategyService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(StrategyPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IStrategyService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(StrategyPermissions.Delete);

        return app;
    }
}
