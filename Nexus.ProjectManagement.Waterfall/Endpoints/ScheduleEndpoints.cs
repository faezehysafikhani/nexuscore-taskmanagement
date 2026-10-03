using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ScheduleEndpoints
{
    /// <summary>Mapped by <see cref="ActivityEndpoints.MapWaterfallEndpoints"/>, so a host that
    /// already maps Waterfall picks these up without a new call.</summary>
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/schedule").WithTags("Waterfall Schedule").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IScheduleService service, CancellationToken cancellationToken) =>
                (await service.GetScheduleAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/apply", async (Guid projectId, IScheduleService service, CancellationToken cancellationToken) =>
                (await service.ApplyScheduleAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
