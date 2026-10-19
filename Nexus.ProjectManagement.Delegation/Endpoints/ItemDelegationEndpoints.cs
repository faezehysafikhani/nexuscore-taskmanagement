using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Application.Dtos;
using Nexus.ProjectManagement.Delegation.Domain;
using Nexus.ProjectManagement.Delegation.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Delegation.Endpoints;

public static class ItemDelegationEndpoints
{
    public static IEndpointRouteBuilder MapItemDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/delegations").WithTags("Item Delegation").RequireAuthorization();

        group.MapGet("/", async (bool? activeOnly, ICurrentUserContext currentUser, IItemDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListMineAsync(currentUser.TenantId.Value, currentUser.UserId.Value, activeOnly ?? true, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ItemDelegationPermissions.View);

        // The trail of hand-overs of one item or of a project, ended ones included. Everyone with View sees their own;
        // whoever may manage others' delegations sees all of them.
        group.MapGet("/history", async (
                Guid? projectId, DelegatedItemType? itemType, Guid? itemId,
                ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext,
                IItemDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canViewAll = (await authorization.AuthorizeAsync(httpContext.User, ItemDelegationPermissions.Manage)).Succeeded;
                return (await service.ListHistoryAsync(currentUser.TenantId.Value, currentUser.UserId.Value, canViewAll, projectId, itemType, itemId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ItemDelegationPermissions.View);

        group.MapPost("/", async (DelegateItemRequest request, ICurrentUserContext currentUser, IItemDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.DelegateAsync(currentUser.TenantId.Value, currentUser.UserId.Value, request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ItemDelegationPermissions.Delegate);

        group.MapPost("/{id:guid}/end", async (
                Guid id, ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext,
                IItemDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canManageOthers = (await authorization.AuthorizeAsync(httpContext.User, ItemDelegationPermissions.Manage)).Succeeded;
                return (await service.EndAsync(currentUser.TenantId.Value, id, currentUser.UserId.Value, canManageOthers, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ItemDelegationPermissions.Delegate);

        return app;
    }
}
