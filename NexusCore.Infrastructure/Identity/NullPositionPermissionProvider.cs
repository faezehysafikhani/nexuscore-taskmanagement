using NexusCore.Application.Identity.Interfaces;

namespace NexusCore.Infrastructure.Identity;

/// <summary>Registered when the organization structure is not part of the host: contributes nothing.</summary>
public sealed class NullPositionPermissionProvider : IPositionPermissionProvider
{
    public Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetGrantingPositionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>>());
}
