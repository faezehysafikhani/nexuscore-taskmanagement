using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

/// <summary>
/// Single-use, time-limited token that authorises a password reset.
/// Only the SHA-256 hash of the token is persisted, mirroring <see cref="RefreshToken"/>.
/// </summary>
public sealed class PasswordResetToken : Entity<Guid>
{
    private PasswordResetToken() : base(Guid.Empty)
    {
        TokenHash = string.Empty;
    }

    public PasswordResetToken(Guid id, Guid userId, string tokenHash, DateTimeOffset expiresAtUtc, string? requestedByIp) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RequestedByIp = requestedByIp;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? RequestedByIp { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public DateTimeOffset? InvalidatedAtUtc { get; private set; }

    /// <summary>A token is usable only while it is unused, not invalidated and not expired.</summary>
    public bool IsActive => UsedAtUtc is null && InvalidatedAtUtc is null && DateTimeOffset.UtcNow < ExpiresAtUtc;

    /// <summary>Consumes the token. A consumed token can never be replayed.</summary>
    public void MarkUsed(DateTimeOffset usedAtUtc) => UsedAtUtc = usedAtUtc;

    /// <summary>Invalidates an outstanding token without consuming it (e.g. superseded by a newer request).</summary>
    public void Invalidate(DateTimeOffset invalidatedAtUtc) => InvalidatedAtUtc = invalidatedAtUtc;
}
