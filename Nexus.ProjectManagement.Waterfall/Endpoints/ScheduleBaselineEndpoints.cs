using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ScheduleBaselineEndpoints
{
    /// <summary>Mapped by <see cref="ActivityEndpoints.MapWaterfallEndpoints"/>.</summary>
    public static IEndpointRouteBuilder MapScheduleBaselineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/baselines").WithTags("Waterfall Baselines").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IScheduleBaselineService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IScheduleBaselineService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapGet("/{id:guid}/variance", async (Guid id, IScheduleBaselineService service, CancellationToken cancellationToken) =>
                (await service.GetVarianceAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/", async (CreateScheduleBaselineRequest request, IScheduleBaselineService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        group.MapDelete("/{id:guid}", async (Guid id, IScheduleBaselineService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
