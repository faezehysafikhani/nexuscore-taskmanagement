using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

public interface IIdentityService
{
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// A sign-in from a browser that still holds a live session of the same user (its refresh-token
    /// cookie, or the access token's sid): that session is closed first, so it never counts toward
    /// the concurrent-session limit nor shows in the list of sessions to close.
    /// </summary>
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? deviceRefreshToken, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> CompleteLoginAsync(NexusCore.Application.Identity.Sessions.CompleteLoginRequest request, CancellationToken cancellationToken);
    Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<PagedResult<UserDto>>> ListUsersAsync(Guid? tenantId, int? pageNumber, int? pageSize, string? search, CancellationToken cancellationToken);
    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> SetUserStatusAsync(Guid userId, SetUserStatusRequest request, CancellationToken cancellationToken);
    Task<Result<UserPermissionsDto>> GetUserAccessAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> SetUserDirectPermissionsAsync(Guid userId, AssignUserPermissionsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<Result<RoleDto>> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<Result> AssignPermissionsAsync(Guid roleId, AssignRolePermissionsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<PermissionGroupDto>>> ListPermissionsGroupedAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TenantDto>>> ListTenantsAsync(CancellationToken cancellationToken);
    Task<Result<TenantDto>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken);
}
