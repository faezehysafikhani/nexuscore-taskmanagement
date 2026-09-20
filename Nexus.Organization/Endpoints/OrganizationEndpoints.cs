using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Organization.Application;
using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Organization.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organization/units").WithTags("Organization").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, IOrganizationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(OrganizationPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IOrganizationService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(OrganizationPermissions.View);

        group.MapPost("/", async (CreateOrganizationUnitRequest request, IOrganizationService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(OrganizationPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateOrganizationUnitRequest request, IOrganizationService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(OrganizationPermissions.Update);

        group.MapDelete("/{id:guid}", async (Guid id, IOrganizationService service, CancellationToken cancellationToken) =>
                (await service.DeactivateAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(OrganizationPermissions.Delete);

        return app;
    }
}
