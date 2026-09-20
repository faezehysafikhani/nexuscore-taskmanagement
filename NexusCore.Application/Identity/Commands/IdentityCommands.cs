using NexusCore.Application.Identity.Dtos;

namespace NexusCore.Application.Identity.Commands;

public sealed record LoginCommand(LoginRequest Request);
public sealed record RefreshTokenCommand(RefreshTokenRequest Request);
public sealed record CreateUserCommand(CreateUserRequest Request);
public sealed record UpdateUserCommand(Guid UserId, UpdateUserRequest Request);
public sealed record AssignUserRolesCommand(Guid UserId, AssignUserRolesRequest Request);
public sealed record CreateRoleCommand(CreateRoleRequest Request);
public sealed record UpdateRoleCommand(Guid RoleId, UpdateRoleRequest Request);
public sealed record AssignRolePermissionsCommand(Guid RoleId, AssignRolePermissionsRequest Request);
public sealed record CreateTenantCommand(CreateTenantRequest Request);
