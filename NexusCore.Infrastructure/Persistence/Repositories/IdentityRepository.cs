using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class IdentityRepository(NexusCoreDbContext dbContext) : IIdentityRepository
{
    public Task<User?> GetUserByEmailAsync(string email, string? tenantSlug, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var query = IncludeUserGraph(dbContext.Users).Where(user => user.Email == normalizedEmail);

        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            query = query.Where(user => user.Tenant != null && user.Tenant.Slug == tenantSlug);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        IncludeUserGraph(dbContext.Users).SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<PagedResult<User>> ListUsersAsync(
    Guid? tenantId,
    int? pageNumber,
    int? pageSize,
    string? search,
    CancellationToken cancellationToken)
    {
        var query = IncludeUserGraph(dbContext.Users).AsQueryable();

        if (tenantId.HasValue)
        {
            query = query.Where(user => user.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.Email.Contains(search) ||
                user.DisplayName.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);

        query = query.OrderBy(user => user.DisplayName);

        List<User> items;

        if (pageNumber.HasValue && pageSize.HasValue)
        {
            var safePageNumber = Math.Max(1, pageNumber.Value);
            var safePageSize = Math.Clamp(pageSize.Value, 1, 100);

            items = await query
                .Skip((safePageNumber - 1) * safePageSize)
                .Take(safePageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<User>(
                items,
                safePageNumber,
                safePageSize,
                total);
        }

        items = await query.ToListAsync(cancellationToken);

        return new PagedResult<User>(
            items,
            1,
            total,
            total);
    }

    public Task<bool> UserEmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return dbContext.Users.AnyAsync(user => user.TenantId == tenantId && user.Email == normalizedEmail, cancellationToken);
    }

    public async Task AddUserAsync(User user, CancellationToken cancellationToken) => await dbContext.Users.AddAsync(user, cancellationToken);

    public async Task RemoveUserAsync(User user, CancellationToken cancellationToken)
    {
        await dbContext.UserGroupMembers
            .Where(member => member.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.Users.Remove(user);
    }

    public async Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);

    public Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken) =>
        IncludeRoleGraph(dbContext.Roles).SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = IncludeRoleGraph(dbContext.Roles).AsQueryable();
        if (tenantId.HasValue)
        {
            query = query.Where(role => role.TenantId == tenantId);
        }

        return await query.OrderBy(role => role.Name).ToListAsync(cancellationToken);
    }

    public Task<bool> RoleNameExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToUpperInvariant();
        return dbContext.Roles.AnyAsync(role => role.TenantId == tenantId && role.NormalizedName == normalizedName, cancellationToken);
    }

    public async Task AddRoleAsync(Role role, CancellationToken cancellationToken) => await dbContext.Roles.AddAsync(role, cancellationToken);

    public async Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken cancellationToken) =>
        await dbContext.Permissions.OrderBy(permission => permission.Module).ThenBy(permission => permission.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) =>
        await dbContext.Tenants.OrderBy(tenant => tenant.Name).ToListAsync(cancellationToken);

    public Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);

    public Task<bool> TenantSlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Tenants.AnyAsync(tenant => tenant.Slug == slug, cancellationToken);

    public async Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken) => await dbContext.Tenants.AddAsync(tenant, cancellationToken);

    public async Task<IReadOnlyList<string>> GetUserPermissionNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(user => user.Id == userId)
            .SelectMany(user => user.Roles)
            .SelectMany(userRole => userRole.Role!.Permissions)
            .Select(rolePermission => rolePermission.Permission!.Name)
            .Distinct()
            .OrderBy(permission => permission)
            .ToListAsync(cancellationToken);

    public Task<RefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Include(token => token.User)!.ThenInclude(user => user!.Roles).ThenInclude(role => role.Role)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash && token.RevokedAtUtc == null && token.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);

    private static IQueryable<User> IncludeUserGraph(IQueryable<User> query) =>
        query.Include(user => user.Tenant)
            .Include(user => user.Roles)
            .ThenInclude(userRole => userRole.Role);

    private static IQueryable<Role> IncludeRoleGraph(IQueryable<Role> query) =>
        query.Include(role => role.Permissions)
            .ThenInclude(rolePermission => rolePermission.Permission);
}
