using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Interfaces;

public interface IUserGroupRepository
{
    Task<IReadOnlyList<UserGroup>> ListAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<UserGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid tenantId, string normalizedName, Guid? excludeGroupId, CancellationToken cancellationToken);
    Task AddAsync(UserGroup group, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
}
