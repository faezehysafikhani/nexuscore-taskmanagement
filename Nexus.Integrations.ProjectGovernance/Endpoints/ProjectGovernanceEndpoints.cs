using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Integrations.ProjectGovernance.Application;
using Nexus.Integrations.ProjectGovernance.Permissions;
using Nexus.ProjectManagement.Core.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Integrations.ProjectGovernance.Endpoints;

public static class ProjectGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapProjectGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations/project-governance").WithTags("Project Governance").RequireAuthorization();

        // Read by every signed-in user (a screen needs to know what it may show); changed only by those who configure governance.
        group.MapGet("/settings", async (ICurrentUserContext currentUser, IGovernanceSettingsService service, CancellationToken cancellationToken) =>
            currentUser.TenantId is null ? Results.Unauthorized() : (await service.GetAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult());

        group.MapPut("/settings", async (GovernanceSettingsDto request, ICurrentUserContext currentUser, IGovernanceSettingsService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is null ? Results.Unauthorized() : (await service.SaveAsync(currentUser.TenantId.Value, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectGovernancePermissions.Configure);

        group.MapGet("/projects", async (ICurrentUserContext currentUser, IGovernanceSettingsService service, CancellationToken cancellationToken) =>
            currentUser.TenantId is null ? Results.Unauthorized() : (await service.ListProjectSettingsAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult());

        group.MapGet("/projects/{projectId:guid}/settings", async (Guid projectId, ICurrentUserContext currentUser, IGovernanceSettingsService service, CancellationToken cancellationToken) =>
            currentUser.TenantId is null ? Results.Unauthorized() : (await service.GetProjectAsync(currentUser.TenantId.Value, projectId, cancellationToken)).ToApiResult());

        group.MapPut("/projects/{projectId:guid}/settings", async (Guid projectId, SaveProjectGovernanceSettingsRequest request, ICurrentUserContext currentUser, IGovernanceSettingsService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is null ? Results.Unauthorized() : (await service.SaveProjectAsync(currentUser.TenantId.Value, projectId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ProjectPermissions.Edit);

        return app;
    }
}
