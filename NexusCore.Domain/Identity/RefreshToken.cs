using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken() : base(Guid.Empty)
    {
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid id, Guid userId, string tokenHash, DateTimeOffset expiresAtUtc, string? createdByIp, Guid? familyId = null) : base(id)
    {
        UserId = userId;
        FamilyId = familyId ?? id;
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

    /// <summary>
    /// The sign-in this token descends from: every token a refresh hands out carries the family
    /// of the token it replaced. A token that was already replaced showing up again means a copy
    /// of it exists, and the whole family is revoked. Null on rows from before this column; those
    /// count as a family of their own (<see cref="Family"/>).
    /// </summary>
    public Guid? FamilyId { get; private set; }

    /// <summary>When the token was exchanged for the next one (null: never, or revoked some other way).</summary>
    public DateTimeOffset? UsedAtUtc { get; private set; }

    public Guid Family => FamilyId ?? Id;
    public bool IsActive => RevokedAtUtc is null && DateTimeOffset.UtcNow < ExpiresAtUtc;

    public void Revoke(string? revokedByIp, string? replacedByTokenHash = null)
    {
        RevokedAtUtc = DateTimeOffset.UtcNow;
        RevokedByIp = revokedByIp;
        ReplacedByTokenHash = replacedByTokenHash;
        if (replacedByTokenHash is not null)
        {
            UsedAtUtc = RevokedAtUtc;
        }
    }
}
