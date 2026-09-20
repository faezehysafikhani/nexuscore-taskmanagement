using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Application.Dtos;
using Nexus.ProjectManagement.Team.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Team.Endpoints;

public static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/project-management/team/members").WithTags("Project Team").RequireAuthorization();

        members.MapGet("/", async (Guid projectId, ITeamService service, CancellationToken cancellationToken) =>
                (await service.ListMembersAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.View);

        members.MapGet("/available-users", async (ICurrentUserContext currentUser, Guid projectId, ITeamService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAvailableUsersAsync(currentUser.TenantId.Value, projectId, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(TeamPermissions.View);

        members.MapPost("/", async (AddProjectMemberRequest request, ITeamService service, CancellationToken cancellationToken) =>
                (await service.AddMemberAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.ManageMembers);

        members.MapDelete("/{memberId:guid}", async (Guid memberId, ITeamService service, CancellationToken cancellationToken) =>
                (await service.RemoveMemberAsync(memberId, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.ManageMembers);

        var governance = app.MapGroup("/api/project-management/team/governance-roles").WithTags("Project Governance").RequireAuthorization();

        governance.MapGet("/", async (Guid projectId, ITeamService service, CancellationToken cancellationToken) =>
                (await service.ListGovernanceRolesAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.View);

        governance.MapPost("/", async (CreateGovernanceRoleRequest request, ITeamService service, CancellationToken cancellationToken) =>
                (await service.CreateGovernanceRoleAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.ManageGovernance);

        governance.MapPut("/{id:guid}", async (Guid id, UpdateGovernanceRoleRequest request, ITeamService service, CancellationToken cancellationToken) =>
                (await service.UpdateGovernanceRoleAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(TeamPermissions.ManageGovernance);

        return app;
    }
}
