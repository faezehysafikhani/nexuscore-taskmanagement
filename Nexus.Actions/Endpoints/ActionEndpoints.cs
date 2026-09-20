using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Actions.Application;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Actions.Endpoints;

public static class ActionEndpoints
{
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/actions").WithTags("Actions").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, Guid? projectId, IActionItemService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, projectId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ActionPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IActionItemService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ActionPermissions.View);

        group.MapPost("/", async (CreateActionItemRequest request, IActionItemService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ActionPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateActionItemRequest request, IActionItemService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ActionPermissions.Edit);

        group.MapPut("/{id:guid}/status", async (Guid id, ChangeActionStatusRequest request, IActionItemService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ActionPermissions.Edit);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IActionItemService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ActionPermissions.Submit);

        return app;
    }
}
