namespace NexusCore.Application.Identity.Dtos;

public sealed record UserDto(Guid Id, Guid TenantId, string Email, string DisplayName, bool IsActive, DateTimeOffset? LastLoginAtUtc, IReadOnlyList<string> Roles);
public sealed record CreateUserRequest(Guid TenantId, string Email, string DisplayName, string Password, bool IsActive = true);
public sealed record UpdateUserRequest(string DisplayName, bool IsActive);
public sealed record AssignUserRolesRequest(IReadOnlyList<Guid> RoleIds);
