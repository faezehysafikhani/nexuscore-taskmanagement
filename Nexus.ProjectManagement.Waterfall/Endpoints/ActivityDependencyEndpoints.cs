using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ActivityDependencyEndpoints
{
    /// <summary>Mapped by <see cref="ActivityEndpoints.MapWaterfallEndpoints"/>, so a host that
    /// already maps Waterfall picks these up without a new call.</summary>
    public static IEndpointRouteBuilder MapActivityDependencyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/dependencies").WithTags("Waterfall Dependencies").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IActivityDependencyService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/", async (CreateActivityDependencyRequest request, IActivityDependencyService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        group.MapPut("/{id:guid}", async (Guid id, UpdateActivityDependencyRequest request, IActivityDependencyService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        group.MapDelete("/{id:guid}", async (Guid id, IActivityDependencyService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
