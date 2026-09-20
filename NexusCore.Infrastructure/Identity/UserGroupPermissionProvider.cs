using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Persistence;

namespace NexusCore.Infrastructure.Identity;

/// <summary>Registered only when the user-group feature is enabled.</summary>
public sealed class UserGroupPermissionProvider(NexusCoreDbContext dbContext) : IUserGroupPermissionProvider
{
    public async Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserGroupMembers
            .Where(member => member.UserId == userId && member.UserGroup!.IsActive)
            .SelectMany(member => member.UserGroup!.Permissions)
            .Select(groupPermission => groupPermission.Permission!.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetGrantingGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var pairs = await dbContext.UserGroupMembers
            .Where(member => member.UserId == userId && member.UserGroup!.IsActive)
            .SelectMany(member => member.UserGroup!.Permissions.Select(groupPermission => new
            {
                groupPermission.PermissionId,
                GroupName = member.UserGroup!.Name
            }))
            .ToListAsync(cancellationToken);

        return pairs
            .GroupBy(pair => pair.PermissionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(pair => pair.GroupName).Distinct().OrderBy(name => name).ToList());
    }
}
