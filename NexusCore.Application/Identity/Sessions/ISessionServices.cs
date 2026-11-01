using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Sessions;

/// <summary>Result of beginning a sign-in: either a new session, or a ticket to choose which session to close.</summary>
public sealed record SessionStart(UserSession? Session, PendingSignInDto? Pending);

/// <summary>Sessions of the signed-in user, and of other users for those allowed to see them.</summary>
public interface ISessionService
{
    /// <summary>
    /// Starts the session of a sign-in whose password was already checked: applies the policy
    /// and the concurrent-session limit. At the limit nothing is created and a ticket comes back.
    /// </summary>
    Task<SessionStart> BeginAsync(User user, IReadOnlyCollection<string> permissions, bool skipLimit, CancellationToken cancellationToken);

    /// <summary>Spends a sign-in ticket and closes the chosen sessions; returns the user to sign in.</summary>
    Task<Result<User>> CompletePendingAsync(CompleteLoginRequest request, CancellationToken cancellationToken);

    Task<Result<SessionListResponse>> ListMineAsync(CancellationToken cancellationToken);
    Task<Result<SessionStatusDto>> GetCurrentStatusAsync(CancellationToken cancellationToken);

    /// <summary>"Keep me signed in": real activity. Resets the idle clock and returns the new status.</summary>
    Task<Result<SessionStatusDto>> RecordActivityAsync(CancellationToken cancellationToken);

    Task<Result> RevokeMineAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<Result<int>> RevokeMyOthersAsync(CancellationToken cancellationToken);
    Task<Result<int>> LogoutAllAsync(CancellationToken cancellationToken);
    Task<Result> UpdateMyPreferencesAsync(UpdateSessionPreferencesRequest request, CancellationToken cancellationToken);

    Task<Result<SessionListResponse>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> RevokeUserSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<Result<int>> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<int>> RevokeAllInOrganizationAsync(RevokeAllSessionsRequest request, CancellationToken cancellationToken);

    Task<Result<SessionPolicySettingsDto>> GetPolicySettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<SessionPolicySettingsDto>> UpdatePolicySettingsAsync(Guid? tenantId, SessionPolicySettingsDto settings, CancellationToken cancellationToken);

    /// <summary>
    /// Ends sessions for a reason the user did not choose (password reset, account disabled...)
    /// and closes their live connections. <paramref name="exceptSessionId"/> keeps one.
    /// </summary>
    Task EndUserSessionsAsync(Guid userId, Guid? exceptSessionId, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>Ends one session (sign-out, reuse of a token...) and closes its live connections.</summary>
    Task EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>Marks sessions whose limits passed as ended (with an audit entry each). Returns how many.</summary>
    Task<int> ExpireDueSessionsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Live connections (SignalR) per session, so that a session that ends takes its connections
/// with it at once instead of at the next call. In-process by design: the system runs on one
/// server, and the hub filter re-checks the session on every call regardless of where it runs.
/// </summary>
public interface ISessionConnectionRegistry
{
    void Close(IEnumerable<Guid> sessionIds);
}

/// <summary>Default for hosts without live connections.</summary>
public sealed class NullSessionConnectionRegistry : ISessionConnectionRegistry
{
    public void Close(IEnumerable<Guid> sessionIds)
    {
    }
}

public enum SessionSecurityEvent
{
    /// <summary>A sign-in from a browser/OS and address range this user has not used before.</summary>
    NewDevice,
    /// <summary>A session was closed from another session, or by an administrator.</summary>
    ClosedFromElsewhere,
    /// <summary>The user hit the concurrent-session limit.</summary>
    LimitReached,
    /// <summary>A refresh token was presented twice: a copy of it exists. Strong signal.</summary>
    TokenReuse,
    /// <summary>A sign-in from outside the organization's internal network. Strong signal.</summary>
    OutsideNetworkSignIn,
    /// <summary>A sign-in on an account that asked to be told of every sign-in.</summary>
    EverySignIn
}

/// <summary>
/// Tells the user about security-relevant session events: in the app for the ordinary ones, by
/// SMS for the strong signals. The Core's default does nothing; the notification integration
/// replaces it. Never throws, never blocks a sign-in.
/// </summary>
public interface ISessionSecurityNotifier
{
    Task NotifyAsync(Guid tenantId, Guid userId, SessionSecurityEvent kind, string? deviceLabel, string? administratorName, CancellationToken cancellationToken);
}

public sealed class NullSessionSecurityNotifier : ISessionSecurityNotifier
{
    public Task NotifyAsync(Guid tenantId, Guid userId, SessionSecurityEvent kind, string? deviceLabel, string? administratorName, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
