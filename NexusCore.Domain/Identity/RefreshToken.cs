using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken() : base(Guid.Empty)
    {
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid id, Guid userId, string tokenHash, DateTimeOffset expiresAtUtc, string? createdByIp) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        CreatedByIp = createdByIp;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? CreatedByIp { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevokedByIp { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public bool IsActive => RevokedAtUtc is null && DateTimeOffset.UtcNow < ExpiresAtUtc;

    public void Revoke(string? revokedByIp, string? replacedByTokenHash = null)
    {
        RevokedAtUtc = DateTimeOffset.UtcNow;
        RevokedByIp = revokedByIp;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
