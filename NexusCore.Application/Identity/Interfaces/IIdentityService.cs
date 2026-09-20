using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

public interface IIdentityService
{
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<PagedResult<UserDto>>> ListUsersAsync(Guid? tenantId, int? pageNumber, int? pageSize, string? search, CancellationToken cancellationToken);
    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<Result<RoleDto>> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<Result> AssignPermissionsAsync(Guid roleId, AssignRolePermissionsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<PermissionGroupDto>>> ListPermissionsGroupedAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TenantDto>>> ListTenantsAsync(CancellationToken cancellationToken);
    Task<Result<TenantDto>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken);
}
