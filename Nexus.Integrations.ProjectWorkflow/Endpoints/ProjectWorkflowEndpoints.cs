using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Integrations.ProjectWorkflow.Application;
using Nexus.Integrations.ProjectWorkflow.Permissions;
using Nexus.Workflow.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Integrations.ProjectWorkflow.Endpoints;

public static class ProjectWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapProjectWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations/project-workflow").WithTags("Project Workflow").RequireAuthorization();

        group.MapGet("/subject-types", () => Results.Ok(ProjectManagementSubjectTypes.All))
            .RequireAuthorization(ProjectWorkflowPermissions.Configure);

        // What an administrator edits: one approval chain per group of things (risks, documents, ...), general or per project.
        group.MapGet("/settings", async (ICurrentUserContext currentUser, Guid? projectId, IProjectWorkflowSettingsService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.GetAsync(currentUser.TenantId.Value, projectId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        group.MapPut("/settings/{groupKey}", async (ICurrentUserContext currentUser, string groupKey, SaveWorkflowGroupRequest request, IProjectWorkflowSettingsService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.SaveGeneralAsync(currentUser.TenantId.Value, groupKey, request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectWorkflowPermissions.Configure);

        group.MapPost("/settings/apply-defaults", async (ICurrentUserContext currentUser, IProjectWorkflowSettingsService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ApplyDefaultsAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectWorkflowPermissions.Configure);

        // A project's own rules: holders of ProjectWorkflow.Configure for any project, otherwise the project's manager or owner for theirs.
        group.MapPut("/projects/{projectId:guid}/settings/{groupKey}", async (
                ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext,
                Guid projectId, string groupKey, SaveWorkflowGroupRequest request, IProjectWorkflowSettingsService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canConfigureAny = (await authorization.AuthorizeAsync(httpContext.User, ProjectWorkflowPermissions.Configure)).Succeeded;
                return (await service.SaveProjectAsync(currentUser.TenantId.Value, projectId, groupKey, request, currentUser.UserId.Value, canConfigureAny, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        group.MapDelete("/projects/{projectId:guid}/settings/{groupKey}", async (
                ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext,
                Guid projectId, string groupKey, IProjectWorkflowSettingsService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canConfigureAny = (await authorization.AuthorizeAsync(httpContext.User, ProjectWorkflowPermissions.Configure)).Succeeded;
                return (await service.ResetProjectAsync(currentUser.TenantId.Value, projectId, groupKey, currentUser.UserId.Value, canConfigureAny, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        group.MapGet("/projects/{projectId:guid}/overrides", async (ICurrentUserContext currentUser, Guid projectId, IProjectWorkflowConfigurationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListProjectOverridesAsync(currentUser.TenantId.Value, projectId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectWorkflowPermissions.Configure);

        group.MapPost("/projects/{projectId:guid}/overrides", async (Guid projectId, CreateProjectWorkflowOverrideRequest request, IProjectWorkflowConfigurationService service, CancellationToken cancellationToken) =>
            {
                var effectiveRequest = request with { ProjectId = projectId };
                return (await service.CreateProjectOverrideAsync(effectiveRequest, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(ProjectWorkflowPermissions.Configure);

        return app;
    }
}
