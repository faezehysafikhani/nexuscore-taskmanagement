namespace NexusCore.Application.Identity.Options;

/// <summary>
/// Identity:Session - how long a sign-in lasts and how the refresh token travels.
///
/// The refresh token is a long-lived credential. A browser keeps it in an HttpOnly cookie, so a
/// script injected into the page cannot read it; only the short-lived access token is in the
/// page (in memory). Clients that are not browsers (scripts, mobile apps) can keep receiving the
/// refresh token in the response body during the transition (<see cref="ReturnRefreshTokenInBody"/>).
/// </summary>
public sealed class SignInSessionOptions
{
    public const string SectionName = "Identity:Session";

    /// <summary>
    /// The longest a sign-in lasts, counted from the password check. A refresh hands out a new
    /// token but keeps this end date, so a stolen token cannot keep a session alive for ever.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// A refresh token that was already exchanged is still honoured for this many seconds (two
    /// tabs, a retry after a dropped connection). After that, seeing it again means a copy of it
    /// exists: the whole sign-in is revoked.
    /// </summary>
    public int ReuseGraceSeconds { get; set; } = 10;

    /// <summary>Name of the refresh token cookie.</summary>
    public string RefreshCookieName { get; set; } = "nexus_rt";

    /// <summary>
    /// Transition: true keeps returning the refresh token in the response body to clients that do
    /// not ask for the cookie (header X-Nexus-Session: cookie). False: every client gets the
    /// cookie only, and the body never carries the token.
    /// </summary>
    public bool ReturnRefreshTokenInBody { get; set; } = true;

    /// <summary>Transition: whether /refresh and /logout still accept the token in the request body. False: cookie only.</summary>
    public bool AcceptRefreshTokenInBody { get; set; } = true;
}
