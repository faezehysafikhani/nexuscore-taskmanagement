using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Waterfall.Endpoints;

public static class MsProjectEndpoints
{
    /// <summary>Largest MS Project file accepted.</summary>
    public const long MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>Mapped by <see cref="ActivityEndpoints.MapWaterfallEndpoints"/>.</summary>
    public static IEndpointRouteBuilder MapMsProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/waterfall/msproject").WithTags("Waterfall MS Project").RequireAuthorization();

        group.MapGet("/export", async (Guid projectId, IMsProjectService service, CancellationToken cancellationToken) =>
            {
                var result = await service.ExportAsync(projectId, cancellationToken);
                return result.IsFailure
                    ? result.ToApiResult()
                    : Results.File(result.Value!.Content, "application/xml", result.Value.FileName);
            })
            .RequireAuthorization(WaterfallPermissions.View);

        group.MapPost("/import", async (
                IFormFile file, ICurrentUserContext currentUser, Guid projectId, bool? replaceExisting,
                IMsProjectService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                if (file.Length is 0 or > MaxFileBytes)
                {
                    return Results.Problem(
                        $"The file must be between 1 byte and {MaxFileBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status400BadRequest);
                }

                await using var stream = file.OpenReadStream();
                return (await service.ImportAsync(currentUser.TenantId.Value, projectId, stream, replaceExisting ?? false, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WaterfallPermissions.ManageSchedule);

        return app;
    }
}
