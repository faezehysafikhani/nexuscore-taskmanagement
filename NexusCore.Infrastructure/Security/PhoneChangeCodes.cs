using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NexusCore.Application.Identity.Security;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// <see cref="IPhoneChangeCodes"/> on <see cref="IDistributedCache"/>, like the sign-in counters:
/// shared between instances as soon as the host registers a distributed cache. Only a salted
/// hash of the code is kept, and the entry expires by itself.
/// </summary>
public sealed class PhoneChangeCodes(IDistributedCache cache) : IPhoneChangeCodes
{
    private static string Key(Guid userId) => $"nexuscore:auth:phone-change:{userId:N}";

    public async Task StoreAsync(Guid userId, string phoneNumber, string code, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var record = new Pending(phoneNumber, salt, Hash(salt, phoneNumber, code));
        await cache.SetStringAsync(Key(userId), JsonSerializer.Serialize(record),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);
    }

    public async Task<bool> ConfirmAsync(Guid userId, string phoneNumber, string code, CancellationToken cancellationToken)
    {
        var raw = await cache.GetStringAsync(Key(userId), cancellationToken);
        var record = raw is null ? null : JsonSerializer.Deserialize<Pending>(raw);
        if (record is null || !string.Equals(record.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            return false;
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(record.CodeHash),
            Encoding.ASCII.GetBytes(Hash(record.Salt, phoneNumber, code)));
        if (matches)
        {
            await cache.RemoveAsync(Key(userId), cancellationToken);
        }

        return matches;
    }

    public Task ClearAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(userId), cancellationToken);

    private static string Hash(string salt, string phoneNumber, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"phone-change\n{salt}\n{phoneNumber}\n{code}")));

    private sealed record Pending(string PhoneNumber, string Salt, string CodeHash);
}
