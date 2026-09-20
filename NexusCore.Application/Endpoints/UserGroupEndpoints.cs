using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;

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

        groups.MapGet("/", async (Guid? tenantId, IUserGroupService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(tenantId, cancellationToken)).ToApiResult())
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

        return app;
    }
}
