using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class ActivityProgressSubmissionEndpoints
{
    /// <summary>Progress reports on activities. Mapped by MapWaterfallEndpoints, so a host that already maps Waterfall picks these up
    /// without a new call. The reports are decided in the approval workflow (subject type "ActivityProgress").</summary>
    public static IEndpointRouteBuilder MapActivityProgressSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/progress-submissions").WithTags("Waterfall Progress Reports").RequireAuthorization();

        group.MapGet("/", async (Guid? projectId, Guid? activityId, IActivityProgressSubmissionService service, CancellationToken cancellationToken) =>
                activityId is { } activity
                    ? (await service.ListByActivityAsync(activity, cancellationToken)).ToApiResult()
                    : projectId is { } project
                        ? (await service.ListByProjectAsync(project, cancellationToken)).ToApiResult()
                        : Microsoft.AspNetCore.Http.Results.BadRequest("Give a projectId or an activityId."))
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/for-projects", async (ListProgressSubmissionsForProjectsRequest request, IActivityProgressSubmissionService service, CancellationToken cancellationToken) =>
                (await service.ListForProjectsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/", async (SubmitActivityProgressRequest request, IActivityProgressSubmissionService service, CancellationToken cancellationToken) =>
                (await service.SubmitAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.SubmitProgress);

        group.MapPut("/{id:guid}/confirmed-progress", async (Guid id, SetConfirmedProgressRequest request, IActivityProgressSubmissionService service, CancellationToken cancellationToken) =>
                (await service.SetConfirmedProgressAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.Edit);

        return app;
    }
}
