namespace NexusCore.AdminUI.Models;

public sealed record LoginRequest(string Identifier, string Password, string? TenantSlug, string? CaptchaId = null, string? CaptchaAnswer = null);
public sealed record CaptchaChallengeDto(string CaptchaId, string ImageDataUrl, int ExpiresInSeconds);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, UserDto User);
public sealed record UserDto(Guid Id, Guid TenantId, string? Email, string DisplayName, bool IsActive, DateTimeOffset? LastLoginAtUtc, IReadOnlyList<string> Roles, string? Username = null, string? PhoneNumber = null);
public sealed record CreateUserRequest(Guid TenantId, string Username, string DisplayName, string Password, bool IsActive = true, string? Email = null, string? PhoneNumber = null);
public sealed record UpdateUserRequest(string DisplayName, bool IsActive);
public sealed record AssignUserRolesRequest(IReadOnlyList<Guid> RoleIds);
public sealed record RoleDto(Guid Id, Guid TenantId, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);
public sealed record CreateRoleRequest(Guid TenantId, string Name, string? Description);
public sealed record AssignRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);
public sealed record PermissionDto(Guid Id, string Name, string Module, string Description);
public sealed record PermissionGroupDto(string Module, IReadOnlyList<PermissionDto> Permissions);
public sealed record TenantDto(Guid Id, string Name, string Slug, string? Description, bool IsActive);
public sealed record CreateTenantRequest(string Name, string Slug, string? Description);
public sealed record AuditLogDto(Guid Id, Guid? TenantId, Guid? UserId, string Action, string? EntityName, string? EntityId, string? Details, string? IpAddress, DateTimeOffset OccurredAtUtc);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages);
