using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class UserGroupRepository(NexusCoreDbContext dbContext) : IUserGroupRepository
{
    public async Task<IReadOnlyList<UserGroup>> ListAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = IncludeGraph(dbContext.UserGroups.AsQueryable());
        if (tenantId.HasValue)
        {
            query = query.Where(group => group.TenantId == tenantId);
        }

        return await query.OrderBy(group => group.Name).ToListAsync(cancellationToken);
    }

    public Task<UserGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        IncludeGraph(dbContext.UserGroups.AsQueryable()).SingleOrDefaultAsync(group => group.Id == groupId, cancellationToken);

    public Task<bool> NameExistsAsync(Guid tenantId, string normalizedName, Guid? excludeGroupId, CancellationToken cancellationToken) =>
        dbContext.UserGroups.AnyAsync(
            group => group.TenantId == tenantId
                && group.NormalizedName == normalizedName
                && (excludeGroupId == null || group.Id != excludeGroupId),
            cancellationToken);

    public async Task AddAsync(UserGroup group, CancellationToken cancellationToken) =>
        await dbContext.UserGroups.AddAsync(group, cancellationToken);

    public async Task<IReadOnlyList<User>> ListUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

    private static IQueryable<UserGroup> IncludeGraph(IQueryable<UserGroup> query) =>
        query.Include(group => group.Permissions)
            .ThenInclude(groupPermission => groupPermission.Permission)
            .Include(group => group.Members);
}
