using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ProgressCurveEndpoints
{
    /// <summary>Mapped by <see cref="ActivityEndpoints.MapWaterfallEndpoints"/>.</summary>
    public static IEndpointRouteBuilder MapProgressCurveEndpoints(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/api/project-management/waterfall").WithTags("Waterfall Progress Curve").RequireAuthorization();

        root.MapGet("/s-curve", async (Guid projectId, int? stepDays, IProgressCurveService service, CancellationToken cancellationToken) =>
                (await service.GetSCurveAsync(projectId, stepDays ?? ProgressCurveService.DefaultStepDays, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        var snapshots = root.MapGroup("/progress-snapshots");

        snapshots.MapGet("/", async (Guid projectId, IProgressCurveService service, CancellationToken cancellationToken) =>
                (await service.ListSnapshotsAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        snapshots.MapPost("/", async (CreateProgressSnapshotRequest request, IProgressCurveService service, CancellationToken cancellationToken) =>
                (await service.CreateSnapshotAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        snapshots.MapDelete("/{id:guid}", async (Guid id, IProgressCurveService service, CancellationToken cancellationToken) =>
                (await service.DeleteSnapshotAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
