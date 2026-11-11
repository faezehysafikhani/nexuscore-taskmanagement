using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

/// <summary>
/// A credential of a service account (a non-human account for another system's calls). The key is shown once; only the hash
/// of its secret is stored. It is exchanged for an ordinary short-lived access token, so every rule of a signed-in user
/// (permissions, session, disabled accounts) applies to it as well.
/// </summary>
public sealed class ApiKey : Entity<Guid>
{
    private ApiKey() : base(Guid.Empty)
    {
        Name = string.Empty;
        SecretHash = string.Empty;
    }

    public ApiKey(Guid id, Guid userId, Guid tenantId, string name, string secretHash, DateTimeOffset nowUtc, DateTimeOffset? expiresAtUtc, Guid? createdByUserId) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        Name = name.Trim();
        SecretHash = secretHash;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByUserId = createdByUserId;
    }

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string SecretHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }

    public bool IsUsableAt(DateTimeOffset nowUtc) => RevokedAtUtc is null && (ExpiresAtUtc is null || nowUtc < ExpiresAtUtc);

    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc ??= nowUtc;

    public void MarkUsed(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;
}
