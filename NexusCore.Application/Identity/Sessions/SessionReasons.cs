using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Sessions;

/// <summary>
/// The codes a client receives when a session is over. They travel in the
/// <c>X-Nexus-Session-Reason</c> header of a rejected request (and in the error code of a failed
/// refresh), so the UI can say why the user was signed out.
/// </summary>
public static class SessionReasons
{
    public const string HeaderName = "X-Nexus-Session-Reason";

    public const string Missing = "session.missing";
    public const string Revoked = "session.revoked";
    public const string IdleExpired = "session.idle_expired";
    public const string AbsoluteExpired = "session.absolute_expired";
    public const string CredentialsChanged = "session.credentials_changed";
    public const string Administrator = "session.administrator";
    public const string AccountDisabled = "session.account_disabled";
    public const string ReuseDetected = "session.reuse_detected";
    public const string ConcurrentLimit = "session.concurrent_limit";
    public const string LoggedOut = "session.logged_out";

    public static string CodeFor(SessionEndReason reason) => reason switch
    {
        SessionEndReason.Logout => LoggedOut,
        SessionEndReason.IdleTimeout => IdleExpired,
        SessionEndReason.AbsoluteTimeout => AbsoluteExpired,
        SessionEndReason.CredentialsChanged => CredentialsChanged,
        SessionEndReason.Administrator => Administrator,
        SessionEndReason.AccountDisabled => AccountDisabled,
        SessionEndReason.ReuseDetected => ReuseDetected,
        SessionEndReason.ConcurrentLimit => ConcurrentLimit,
        _ => Revoked
    };
}
