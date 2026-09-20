using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Deliverables.Application.Dtos;
using Nexus.ProjectManagement.Deliverables.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Deliverables.Endpoints;

public static class DeliverableEndpoints
{
    public static IEndpointRouteBuilder MapDeliverableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/deliverables").WithTags("Deliverables").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IDeliverableService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(DeliverablePermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IDeliverableService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(DeliverablePermissions.View);

        group.MapPost("/", async (CreateDeliverableRequest request, IDeliverableService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(DeliverablePermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateDeliverableRequest request, IDeliverableService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(DeliverablePermissions.Edit);

        group.MapPut("/{id:guid}/status", async (Guid id, ChangeDeliverableStatusRequest request, IDeliverableService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(DeliverablePermissions.Edit);

        return app;
    }
}
