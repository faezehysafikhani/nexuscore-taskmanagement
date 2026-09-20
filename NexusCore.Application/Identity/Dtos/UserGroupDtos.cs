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
    IReadOnlyList<UserGroupMemberDto> Members);

public sealed record UserGroupMemberDto(Guid UserId, string DisplayName, string Email);

public sealed record CreateUserGroupRequest(Guid TenantId, string Name, string? Description);

public sealed record UpdateUserGroupRequest(string Name, string? Description, bool IsActive);

public sealed record AssignGroupPermissionsRequest(IReadOnlyList<Guid> PermissionIds);

public sealed record AssignGroupMembersRequest(IReadOnlyList<Guid> UserIds);
