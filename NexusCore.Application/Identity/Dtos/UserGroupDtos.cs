namespace NexusCore.Application.Identity.Dtos;

public sealed record UserGroupDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    bool IsActive,
    int MemberCount,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> PermissionIds,
    IReadOnlyList<UserGroupMemberDto> Members,
    Guid? OwnerUserId = null);

public sealed record UserGroupMemberDto(Guid UserId, string DisplayName, string Email);

public sealed record CreateUserGroupRequest(Guid TenantId, string Name, string? Description);

public sealed record UpdateUserGroupRequest(string Name, string? Description, bool IsActive);

public sealed record AssignGroupPermissionsRequest(IReadOnlyList<Guid> PermissionIds);

public sealed record AssignGroupMembersRequest(IReadOnlyList<Guid> UserIds);

/// <summary>A personal work team of the signed-in user.</summary>
public sealed record CreateMyTeamRequest(string Name, string? Description = null);

public sealed record UpdateMyTeamRequest(string Name, string? Description = null);

/// <summary>The complete member list. The owner always stays a member.</summary>
public sealed record SetMyTeamMembersRequest(IReadOnlyList<Guid> UserIds);
