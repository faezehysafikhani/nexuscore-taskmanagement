using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Contracts.Endpoints;

public static class ChangeRequestEndpoints
{
    /// <summary>Change requests of a project. Reading needs Contracts.View; writing, submitting, implementing or cancelling one Contracts.ManageChanges.</summary>
    public static IEndpointRouteBuilder MapChangeRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/change-requests").WithTags("ChangeRequests").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        // The project's changes at a glance: pending decisions, approved impact against the baseline, and changes made without approval.
        group.MapGet("/dashboard", async (Guid projectId, IChangeDashboardService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/", async (CreateChangeRequestRequest request, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        group.MapPut("/{id:guid}", async (Guid id, SaveChangeRequestRequest request, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        group.MapDelete("/{id:guid}", async (Guid id, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        group.MapPost("/{id:guid}/submit", async (Guid id, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.SubmitAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        group.MapPost("/{id:guid}/implement", async (Guid id, ImplementChangeRequestRequest request, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.ImplementAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelChangeRequestRequest request, IChangeRequestService service, CancellationToken cancellationToken) =>
                (await service.CancelAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageChanges);

        return app;
    }
}
