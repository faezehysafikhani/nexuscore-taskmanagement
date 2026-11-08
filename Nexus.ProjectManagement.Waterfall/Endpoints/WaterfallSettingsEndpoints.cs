using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class WaterfallSettingsEndpoints
{
    /// <summary>How a project weighs its activities and reads planned progress. Mapped by MapActivityEndpoints, so a host that
    /// already maps Waterfall picks these up without a new call.</summary>
    public static IEndpointRouteBuilder MapWaterfallSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/settings").WithTags("Waterfall Settings").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IWaterfallSettingsService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/for-projects", async (WaterfallSettingsForProjectsRequest request, IWaterfallSettingsService service, CancellationToken cancellationToken) =>
                (await service.GetForProjectsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPut("/", async (SaveWaterfallSettingsRequest request, IWaterfallSettingsService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
