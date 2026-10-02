using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Endpoints;

/// <summary>
/// Optional user-group feature. Program.cs only calls MapUserGroupEndpoints when
/// Features:UserGroups:Enabled is true, so with the feature off these routes do not exist
/// and every group request returns 404.
/// </summary>
public static class UserGroupEndpoints
{
    public static IEndpointRouteBuilder MapUserGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var groups = app.MapGroup("/api/identity/groups").WithTags("Identity - Groups");

        // Without an explicit tenantId the list is the caller's own tenant - never every tenant.
        groups.MapGet("/", async (Guid? tenantId, ICurrentUserContext currentUser, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(tenantId ?? currentUser.TenantId, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsView);

        groups.MapGet("/{groupId:guid}", async (Guid groupId, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(groupId, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsView);

        groups.MapPost("/", async (CreateUserGroupRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsCreate);

        groups.MapPut("/{groupId:guid}", async (Guid groupId, UpdateUserGroupRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(groupId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsUpdate);

        groups.MapPut("/{groupId:guid}/permissions", async (Guid groupId, AssignGroupPermissionsRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.AssignPermissionsAsync(groupId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsAssignPermissions);

        groups.MapPut("/{groupId:guid}/members", async (Guid groupId, AssignGroupMembersRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.AssignMembersAsync(groupId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsManageMembers);

        groups.MapDelete("/{groupId:guid}", async (Guid groupId, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(groupId, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsDelete);

        // Personal work teams ("my teams" in the task UI). Every route acts on the caller's own
        // teams only; the owner comes from the token, never from the request.
        var mine = groups.MapGroup("/mine").WithTags("Identity - My teams");

        // Seeing the teams you belong to (owner or plain member) is not an administrative act -
        // it only ever returns your own membership (IUserGroupService.ListMyTeamsAsync scopes to
        // the caller). Chat needs this to show a real member their Team Conversation, so it must
        // not require GroupsManageOwn (creating/editing a team) or any other admin permission.
        mine.MapGet("/", async (IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.ListMyTeamsAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization();

        mine.MapPost("/", async (CreateMyTeamRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.CreateMyTeamAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsManageOwn);

        mine.MapPut("/{groupId:guid}", async (Guid groupId, UpdateMyTeamRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.UpdateMyTeamAsync(groupId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsManageOwn);

        mine.MapPut("/{groupId:guid}/members", async (Guid groupId, SetMyTeamMembersRequest request, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.SetMyTeamMembersAsync(groupId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsManageOwn);

        mine.MapDelete("/{groupId:guid}", async (Guid groupId, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.DeleteMyTeamAsync(groupId, cancellationToken)).ToApiResult())
            .RequireAuthorization(UserGroupPermissions.GroupsManageOwn);

        return app;
    }
}
