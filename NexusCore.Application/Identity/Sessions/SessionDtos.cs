namespace NexusCore.Application.Identity.Sessions;

/// <summary>One session as its owner (or an administrator) sees it.</summary>
public sealed record SessionDto(
    Guid Id,
    Guid UserId,
    string? DeviceLabel,
    string? UserAgent,
    string? CreatedIp,
    string? LastIp,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    string PolicyProfile,
    bool IsCurrent,
    bool IsActive,
    DateTimeOffset? EndedAtUtc,
    string? EndReason,
    string? EndedByName);

public sealed record SessionPolicyInfo(string Profile, int IdleSeconds, int AbsoluteSeconds, int MaxConcurrentAllowed, int MaxConcurrentChosen);

public sealed record SessionListResponse(
    IReadOnlyList<SessionDto> Active,
    IReadOnlyList<SessionDto> Recent,
    SessionPolicyInfo? Policy,
    bool SmsOnEveryNewSignIn);

/// <summary>
/// The live state of the caller's own session, in seconds: how long until it ends from inactivity
/// and how long until it ends for good. The client builds its warnings from these, never from its own clock alone.
/// </summary>
public sealed record SessionStatusDto(
    Guid SessionId,
    int IdleTimeoutSeconds,
    int IdleRemainingSeconds,
    int AbsoluteRemainingSeconds,
    DateTimeOffset ServerTimeUtc,
    int IdleWarningSeconds = 120);

public sealed record UpdateSessionPreferencesRequest(int? MaxConcurrentSessions, bool SmsOnEveryNewSignIn);

/// <summary>Step 2 of a sign-in that stopped at the session limit. Close sessions by id, or let the oldest ones go.</summary>
public sealed record CompleteLoginRequest(string Ticket, IReadOnlyList<Guid>? CloseSessionIds = null, bool CloseOldest = false);

/// <summary>Returned instead of tokens when the limit is reached. The ticket is single-use and lasts two minutes.</summary>
public sealed record PendingSignInDto(
    string Ticket,
    DateTimeOffset ExpiresAtUtc,
    int MaxConcurrent,
    int MustClose,
    IReadOnlyList<SessionDto> Sessions);

public sealed record RevokeAllSessionsRequest(string Password, string Reason);

public sealed record SessionPolicySettingsDto(
    int IdleMinutes, int AbsoluteHours, int MaxConcurrent,
    int SensitiveIdleMinutes, int SensitiveAbsoluteHours, int SensitiveMaxConcurrent,
    int SystemIdleMinutes, int SystemAbsoluteHours, int SystemMaxConcurrent,
    IReadOnlyList<string> InternalNetworks, int ExternalIdleMinutes, int ExternalAbsoluteHours,
    SessionPolicyRange Range);

public sealed record SessionPolicyRange(int MinIdleMinutes, int MaxIdleMinutes, int MinAbsoluteHours, int MaxAbsoluteHours, int MinConcurrent, int MaxConcurrent);
