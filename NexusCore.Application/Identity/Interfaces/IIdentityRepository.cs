using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

public interface IIdentityRepository
{
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the account for a sign-in identifier: a username or a mobile number (in any common
    /// spelling). Email addresses are not sign-in names and never match. Returns null when
    /// nothing matches or the identifier is ambiguous across tenants.
    /// </summary>
    Task<User?> FindUserByLoginAsync(string identifier, string? tenantSlug, CancellationToken cancellationToken);
    Task<bool> UsernameExistsAsync(Guid tenantId, string username, Guid? exceptUserId, CancellationToken cancellationToken);

    /// <summary>Whether another user of the tenant has this mobile number (compared in canonical form).</summary>
    Task<bool> PhoneNumberExistsAsync(Guid tenantId, string phoneNumber, Guid? exceptUserId, CancellationToken cancellationToken);
    Task<Role?> GetRoleByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken);
    Task AddPasswordResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken);
    Task<PasswordResetToken?> FindActivePasswordResetTokenAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The user's newest reset token that is neither used nor withdrawn - expired or not.</summary>
    Task<PasswordResetToken?> FindLatestOutstandingPasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Revokes every active refresh token of the user: signs them out everywhere.</summary>
    Task RevokeRefreshTokensAsync(Guid userId, CancellationToken cancellationToken);
    Task InvalidatePasswordResetTokensAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<PagedResult<User>> ListUsersAsync(Guid? tenantId,
    int? pageNumber,
    int? pageSize,
    string? search,
    CancellationToken cancellationToken,
    IReadOnlyCollection<Guid>? excludeUserIds = null);
    Task<bool> UserEmailExistsAsync(Guid tenantId, string email, Guid? exceptUserId, CancellationToken cancellationToken);
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

    /// <summary>True when the user exists and is enabled. Checked on every authenticated request.</summary>
    Task<bool> IsUserActiveAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Permission>> GetPermissionsByIdsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);
    Task<RefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The refresh token whatever its state (active, replaced, revoked, expired), with its user.</summary>
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the token out of use in one conditional statement (revoked, marked used, pointing to
    /// its successor) and says whether this call was the one that did it. Two requests with the
    /// same token cannot both get true.
    /// </summary>
    Task<bool> TryUseRefreshTokenAsync(Guid tokenId, string replacedByTokenHash, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>The token's state as it is in the database right now, not as this request first read it.</summary>
    Task<RefreshTokenState?> GetRefreshTokenStateAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>Revokes every token of one sign-in (family): the token in use, its successors and its replaced ones.</summary>
    Task RevokeRefreshTokenFamilyAsync(Guid userId, Guid familyId, CancellationToken cancellationToken);

    /// <summary>Signs the user out everywhere except the one sign-in (family) the caller is using.</summary>
    Task RevokeRefreshTokensExceptFamilyAsync(Guid userId, Guid keepFamilyId, CancellationToken cancellationToken);
}

public sealed record RefreshTokenState(DateTimeOffset? RevokedAtUtc, DateTimeOffset? UsedAtUtc, string? ReplacedByTokenHash);
