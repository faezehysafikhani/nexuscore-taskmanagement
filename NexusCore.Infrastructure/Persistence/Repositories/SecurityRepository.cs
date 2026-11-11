using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Security;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class SecurityRepository(NexusCoreDbContext dbContext) : ISecurityRepository
{
    public async Task AddChallengeAsync(MfaChallenge challenge, CancellationToken cancellationToken) =>
        await dbContext.MfaChallenges.AddAsync(challenge, cancellationToken);

    public Task<MfaChallenge?> GetChallengeAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MfaChallenges.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task InvalidateChallengesAsync(Guid userId, MfaPurpose purpose, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // A new code replaces every older one; old, finished challenges are cleared with them.
        var old = await dbContext.MfaChallenges
            .Where(c => c.UserId == userId && (c.Purpose == purpose && c.UsedAtUtc == null || c.ExpiresAtUtc < nowUtc.AddDays(-1)))
            .ToListAsync(cancellationToken);
        dbContext.MfaChallenges.RemoveRange(old);
    }

    public async Task AddApiKeyAsync(ApiKey key, CancellationToken cancellationToken) => await dbContext.ApiKeys.AddAsync(key, cancellationToken);

    public Task<ApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> ListApiKeysAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0 ? [] : await dbContext.ApiKeys.Where(k => userIds.Contains(k.UserId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListServiceAccountsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Users.Where(u => u.TenantId == tenantId && u.Kind == UserKind.ServiceAccount).OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
}
