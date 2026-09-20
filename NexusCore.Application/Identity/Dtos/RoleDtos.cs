namespace NexusCore.Application.Identity.Dtos;

public sealed record RoleDto(Guid Id, Guid TenantId, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);
public sealed record CreateRoleRequest(Guid TenantId, string Name, string? Description);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record AssignRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);
