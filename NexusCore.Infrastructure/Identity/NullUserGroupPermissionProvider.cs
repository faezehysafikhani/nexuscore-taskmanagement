using NexusCore.Application.Identity.Interfaces;

namespace NexusCore.Infrastructure.Identity;

/// <summary>
/// Registered when the user-group feature is disabled. Contributes nothing, so permission
/// resolution is byte-for-byte what it was before the feature was added.
/// </summary>
public sealed class NullUserGroupPermissionProvider : IUserGroupPermissionProvider
{
    public Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetGrantingGroupsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(
            new Dictionary<Guid, IReadOnlyList<string>>());
}
