using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Security;

/// <summary>Storage of the second sign-in step and of API keys.</summary>
public interface ISecurityRepository
{
    Task AddChallengeAsync(MfaChallenge challenge, CancellationToken cancellationToken);
    Task<MfaChallenge?> GetChallengeAsync(Guid id, CancellationToken cancellationToken);
    Task InvalidateChallengesAsync(Guid userId, MfaPurpose purpose, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task AddApiKeyAsync(ApiKey key, CancellationToken cancellationToken);
    Task<ApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiKey>> ListApiKeysAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListServiceAccountsAsync(Guid tenantId, CancellationToken cancellationToken);
}
