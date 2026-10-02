using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Interfaces;

public interface IUserGroupRepository
{
    Task<IReadOnlyList<UserGroup>> ListAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<UserGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    /// <summary>Name clash within the same scope: organisational groups (owner null) or one owner's teams.</summary>
    Task<bool> NameExistsAsync(Guid tenantId, string normalizedName, Guid? excludeGroupId, CancellationToken cancellationToken, Guid? ownerUserId = null);
    Task<IReadOnlyList<UserGroup>> ListOwnedAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserGroup>> ListVisibleToUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    void Remove(UserGroup group);
    Task AddAsync(UserGroup group, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
}
