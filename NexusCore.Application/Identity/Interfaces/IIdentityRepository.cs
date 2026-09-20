using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

public interface IIdentityRepository
{
    Task<User?> GetUserByEmailAsync(string email, string? tenantSlug, CancellationToken cancellationToken);
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<PagedResult<User>> ListUsersAsync(Guid? tenantId,
    int? pageNumber,
    int? pageSize,
    string? search,
    CancellationToken cancellationToken);
    Task<bool> UserEmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken);
    Task AddUserAsync(User user, CancellationToken cancellationToken);
    Task RemoveUserAsync(User user, CancellationToken cancellationToken);
    Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken);
    Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<bool> RoleNameExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken);
    Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken);
    Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> TenantSlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetUserPermissionNamesAsync(Guid userId, CancellationToken cancellationToken);
    Task<RefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);
}
