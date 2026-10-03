using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Workflow.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var definitions = app.MapGroup("/api/workflow/definitions").WithTags("Workflow").RequireAuthorization();

        definitions.MapGet("/", async (ICurrentUserContext currentUser, string? subjectType, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, subjectType, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        definitions.MapGet("/{id:guid}", async (Guid id, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.View);

        definitions.MapPost("/", async (CreateWorkflowDefinitionRequest request, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.Configure);

        definitions.MapPost("/{id:guid}/steps", async (Guid id, AddWorkflowStepRequest request, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.AddStepAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.Configure);

        definitions.MapDelete("/{id:guid}/steps/{stepId:guid}", async (Guid id, Guid stepId, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.DeleteStepAsync(id, stepId, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.Configure);

        definitions.MapPut("/{id:guid}/steps/{stepId:guid}/move", async (Guid id, Guid stepId, MoveWorkflowStepRequest request, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.MoveStepAsync(id, stepId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.Configure);

        definitions.MapPost("/{id:guid}/reset-to-default", async (Guid id, IWorkflowDefinitionService service, CancellationToken cancellationToken) =>
                (await service.ResetToDefaultAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.Configure);

        var approvalCenter = app.MapGroup("/api/workflow/approval-center").WithTags("Workflow - Approval Center").RequireAuthorization();

        approvalCenter.MapGet("/", async (ICurrentUserContext currentUser, IWorkflowInstanceService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListPendingForApproverAsync(currentUser.TenantId.Value, currentUser.UserId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        approvalCenter.MapGet("/{id:guid}", async (Guid id, IWorkflowInstanceService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(WorkflowPermissions.View);

        approvalCenter.MapPost("/{id:guid}/approve", async (Guid id, DecideWorkflowInstanceRequest request, ICurrentUserContext currentUser, IWorkflowInstanceService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ApproveAsync(id, currentUser.UserId.Value, request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.Approve);

        approvalCenter.MapPost("/{id:guid}/reject", async (Guid id, DecideWorkflowInstanceRequest request, ICurrentUserContext currentUser, IWorkflowInstanceService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.RejectAsync(id, currentUser.UserId.Value, request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.Reject);

        // Delegation: handing approvals to a substitute for a period. Anyone who can approve may delegate their own;
        // delegating someone else's, or revoking another person's, needs Workflow.Configure.
        var delegations = app.MapGroup("/api/workflow/delegations").WithTags("Workflow - Delegation").RequireAuthorization();

        delegations.MapGet("/", async (ICurrentUserContext currentUser, IWorkflowDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListMineAsync(currentUser.TenantId.Value, currentUser.UserId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.View);

        delegations.MapPost("/", async (
                CreateWorkflowDelegationRequest request, ICurrentUserContext currentUser,
                IAuthorizationService authorization, HttpContext httpContext,
                IWorkflowDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canManageOthers = (await authorization.AuthorizeAsync(httpContext.User, WorkflowPermissions.Configure)).Succeeded;
                return (await service.CreateAsync(currentUser.TenantId.Value, currentUser.UserId.Value, canManageOthers, request, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.Approve);

        delegations.MapPost("/{id:guid}/revoke", async (
                Guid id, ICurrentUserContext currentUser,
                IAuthorizationService authorization, HttpContext httpContext,
                IWorkflowDelegationService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var canManageOthers = (await authorization.AuthorizeAsync(httpContext.User, WorkflowPermissions.Configure)).Succeeded;
                return (await service.RevokeAsync(currentUser.TenantId.Value, id, currentUser.UserId.Value, canManageOthers, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(WorkflowPermissions.Approve);

        return app;
    }
}
