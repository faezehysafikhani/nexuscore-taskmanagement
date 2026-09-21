using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Persistence;

namespace NexusCore.Infrastructure.Identity;

public sealed class UserDirectory(NexusCoreDbContext dbContext) : IUserDirectory
{
    public async Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.Distinct().ToList();
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new UserContact(
                user.Id,
                user.TenantId,
                user.DisplayName,
                user.Email,
                user.Username,
                user.AvatarUrl,
                user.PhoneNumber,
                user.TelegramChatId,
                user.NotifySms,
                user.NotifyTelegram,
                user.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) =>
        await dbContext.UserGroupMembers
            .AsNoTracking()
            .Where(member => member.UserGroupId == groupId)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);
}
