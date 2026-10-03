using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Progress.Endpoints;

public static class DelayReasonEndpoints
{
    /// <summary>Mapped by <see cref="ProgressEndpoints.MapProgressEndpoints"/>, so a host that
    /// already maps Progress picks these up without a new call.</summary>
    public static IEndpointRouteBuilder MapDelayReasonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/delay-reasons").WithTags("Delay Reasons").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.View);

        group.MapPost("/", async (CreateDelayReasonRequest request, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateDelayReasonRequest request, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IDelayReasonService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(DelayReasonPermissions.Submit);

        return app;
    }
}
