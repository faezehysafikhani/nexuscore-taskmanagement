using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Sessions;

/// <summary>What the per-request check needs to know about a session and its user, in one read.</summary>
public sealed record SessionValidation(
    bool UserIsActive,
    DateTimeOffset? RevokedAtUtc,
    SessionEndReason? RevokedReason,
    DateTimeOffset AbsoluteExpiresAtUtc,
    int IdleTimeoutSeconds,
    DateTimeOffset LastActivityAtUtc)
{
    /// <summary>Null when the session may be used at <paramref name="nowUtc"/>; otherwise the code to tell the client.</summary>
    public string? FailureCode(DateTimeOffset nowUtc)
    {
        if (!UserIsActive)
        {
            return SessionReasons.AccountDisabled;
        }

        if (RevokedAtUtc is not null)
        {
            return SessionReasons.CodeFor(RevokedReason ?? SessionEndReason.Revoked);
        }

        if (nowUtc >= AbsoluteExpiresAtUtc)
        {
            return SessionReasons.AbsoluteExpired;
        }

        return nowUtc >= LastActivityAtUtc.AddSeconds(IdleTimeoutSeconds) ? SessionReasons.IdleExpired : null;
    }
}

public interface ISessionRepository
{
    Task AddSessionAsync(UserSession session, CancellationToken cancellationToken);
    Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The session and its user's state in one query (primary-key lookup); null when the session does not exist for this user.</summary>
    Task<SessionValidation?> ValidateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Notes that the session was used. LastSeenAtUtc moves at most once per <paramref name="minInterval"/>;
    /// LastActivityAtUtc (the idle clock) only when <paramref name="activity"/> is true, with the same
    /// limit. One conditional UPDATE, so a busy page does not turn into a write per request.
    /// </summary>
    Task TouchAsync(Guid sessionId, DateTimeOffset nowUtc, bool activity, string? ipAddress, TimeSpan minInterval, CancellationToken cancellationToken);

    /// <summary>Sessions of the user that can still be used, oldest first.</summary>
    Task<IReadOnlyList<UserSession>> ListActiveSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Sessions of the user that ended since <paramref name="sinceUtc"/>, newest first.</summary>
    Task<IReadOnlyList<UserSession>> ListEndedSessionsAsync(Guid userId, DateTimeOffset nowUtc, DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken);

    /// <summary>Whether the user has had a session from this device label and address range before.</summary>
    Task<bool> HasSeenDeviceAsync(Guid userId, string? deviceLabel, string? addressRange, CancellationToken cancellationToken);

    /// <summary>Ends the session and revokes its refresh tokens. Returns whether it was alive before.</summary>
    Task<bool> RevokeSessionAsync(Guid sessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken);

    /// <summary>Ends every live session of the user (except one) and their refresh tokens; returns the ids that ended.</summary>
    Task<IReadOnlyList<Guid>> RevokeUserSessionsAsync(Guid userId, Guid? exceptSessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken);

    /// <summary>Ends every live session of the organization (except one); returns the ids that ended.</summary>
    Task<IReadOnlyList<Guid>> RevokeTenantSessionsAsync(Guid tenantId, Guid? exceptSessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken);

    /// <summary>Sessions whose idle or absolute limit has passed but which were never marked as ended.</summary>
    Task<IReadOnlyList<UserSession>> ListExpiredUnmarkedAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken);

    /// <summary>Deletes old data: closed sessions, their refresh tokens, used or expired sign-in tickets. Returns rows removed.</summary>
    Task<int> PurgeAsync(DateTimeOffset sessionsEndedBeforeUtc, DateTimeOffset tokensOfEndedSessionsBeforeUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task AddPendingSignInAsync(PendingSignIn pending, CancellationToken cancellationToken);
    Task<PendingSignIn?> FindPendingSignInAsync(string ticketHash, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the ticket used in one conditional statement; true for exactly one caller, so a
    /// ticket cannot be spent twice by two simultaneous requests.
    /// </summary>
    Task<bool> TryUsePendingSignInAsync(Guid pendingId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
