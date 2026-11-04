using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

/// <summary>
/// Why a session ended. Stored as text so a reason added later does not renumber the old rows,
/// and sent to the client as the reason code of a rejected request (<c>session.idle_expired</c>).
/// </summary>
public enum SessionEndReason
{
    /// <summary>The user signed out.</summary>
    Logout = 1,
    /// <summary>The user (or an administrator) closed it from another place.</summary>
    Revoked = 2,
    /// <summary>No real activity for longer than the idle timeout.</summary>
    IdleTimeout = 3,
    /// <summary>The absolute lifetime of the sign-in ended.</summary>
    AbsoluteTimeout = 4,
    /// <summary>The password or mobile number changed.</summary>
    CredentialsChanged = 5,
    /// <summary>An administrator closed it (one user, or the whole organization).</summary>
    Administrator = 6,
    /// <summary>The account was disabled or deleted.</summary>
    AccountDisabled = 7,
    /// <summary>An exchanged refresh token was presented again: a copy of it exists.</summary>
    ReuseDetected = 8,
    /// <summary>The user picked it to make room for a new sign-in at the concurrent-session limit.</summary>
    ConcurrentLimit = 9
}

/// <summary>
/// One sign-in of one user on one device. It owns the family of refresh tokens that descend from
/// the sign-in (<see cref="RefreshToken.FamilyId"/> equals <see cref="Entity{TId}.Id"/>) and is the
/// <c>sid</c> claim of every access token issued for it. Every authenticated request checks it, so
/// revoking or expiring it ends access at once, not when the access token runs out.
///
/// The idle and absolute limits are copied from the policy when the session is created, so a policy
/// change affects new sign-ins only.
/// </summary>
public sealed class UserSession : Entity<Guid>
{
    private UserSession() : base(Guid.Empty)
    {
        PolicyProfile = string.Empty;
    }

    public UserSession(
        Guid id, Guid userId, Guid tenantId, DateTimeOffset nowUtc, DateTimeOffset absoluteExpiresAtUtc,
        int idleTimeoutSeconds, string policyProfile, string? ip, string? userAgent, string? deviceLabel) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        CreatedAtUtc = nowUtc;
        LastActivityAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
        AbsoluteExpiresAtUtc = absoluteExpiresAtUtc;
        IdleTimeoutSeconds = idleTimeoutSeconds;
        PolicyProfile = policyProfile;
        CreatedIp = ip;
        LastIp = ip;
        UserAgent = Limit(userAgent, MaxUserAgentLength);
        DeviceLabel = Limit(deviceLabel, 80);
    }

    public const int MaxUserAgentLength = 512;

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>The last time the user really did something (clicked, typed, scrolled, touched). Drives the idle timeout.</summary>
    public DateTimeOffset LastActivityAtUtc { get; private set; }

    /// <summary>The last request of any kind, polling included. For display only; never extends the session.</summary>
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }
    public int IdleTimeoutSeconds { get; private set; }

    /// <summary>Which policy profile produced the limits (default, sensitive, outside-network, system).</summary>
    public string PolicyProfile { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public SessionEndReason? RevokedReason { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? CreatedIp { get; private set; }
    public string? LastIp { get; private set; }
    public string? UserAgent { get; private set; }
    public string? DeviceLabel { get; private set; }

    public DateTimeOffset IdleExpiresAtUtc => LastActivityAtUtc.AddSeconds(IdleTimeoutSeconds);

    /// <summary>Alive: not revoked and neither limit reached.</summary>
    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        RevokedAtUtc is null && nowUtc < AbsoluteExpiresAtUtc && nowUtc < IdleExpiresAtUtc;

    /// <summary>The reason the session is not usable at this moment, or null while it is.</summary>
    public SessionEndReason? EndReasonAt(DateTimeOffset nowUtc)
    {
        if (RevokedAtUtc is not null)
        {
            return RevokedReason ?? SessionEndReason.Revoked;
        }

        if (nowUtc >= AbsoluteExpiresAtUtc)
        {
            return SessionEndReason.AbsoluteTimeout;
        }

        return nowUtc >= IdleExpiresAtUtc ? SessionEndReason.IdleTimeout : null;
    }

    public void Revoke(SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId = null)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
        RevokedReason = reason;
        RevokedByUserId = byUserId;
    }

    public void RecordActivity(DateTimeOffset nowUtc, string? ip)
    {
        LastActivityAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
        LastIp = ip ?? LastIp;
    }

    private static string? Limit(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// A sign-in that stopped at the concurrent-session limit: the password was right, but no token
/// is issued until the user chooses which of their sessions to close. Single use, two minutes.
/// Only the hash of the ticket is stored.
/// </summary>
public sealed class PendingSignIn : Entity<Guid>
{
    private PendingSignIn() : base(Guid.Empty)
    {
        TicketHash = string.Empty;
    }

    public PendingSignIn(Guid id, Guid userId, string ticketHash, DateTimeOffset nowUtc, TimeSpan lifetime, string? createdIp) : base(id)
    {
        UserId = userId;
        TicketHash = ticketHash;
        ExpiresAtUtc = nowUtc.Add(lifetime);
        CreatedIp = createdIp;
    }

    public Guid UserId { get; private set; }
    public string TicketHash { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public string? CreatedIp { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }

    public bool IsUsableAt(DateTimeOffset nowUtc) => UsedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void MarkUsed(DateTimeOffset nowUtc) => UsedAtUtc = nowUtc;
}
