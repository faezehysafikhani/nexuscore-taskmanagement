using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Options;

namespace NexusCore.Application.Endpoints;

/// <summary>
/// The refresh token in an HttpOnly cookie: the browser keeps it and sends it back to the auth
/// endpoints, and a script running in the page can never read it.
///
/// A client asks for this with the header <c>X-Nexus-Session: cookie</c> on sign-in and refresh
/// (Identity:Session:ReturnRefreshTokenInBody = false makes it the only way). The cookie is
/// SameSite=Strict and limited to /api/identity/auth, and a request that relies on it must also
/// carry <c>X-Requested-With</c>, a header a cross-site form cannot send.
/// </summary>
public static class SessionCookies
{
    public const string ModeHeader = "X-Nexus-Session";
    public const string ModeCookie = "cookie";
    public const string CsrfHeader = "X-Requested-With";
    public const string CookiePath = "/api/identity/auth";

    public static bool WantsCookie(HttpContext http, SignInSessionOptions options) =>
        !options.ReturnRefreshTokenInBody
        || string.Equals(http.Request.Headers[ModeHeader].FirstOrDefault(), ModeCookie, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The refresh token of this request: the body (while still accepted) or else the cookie.
    /// A token that only came from the cookie also needs the CSRF header; without it null.
    /// </summary>
    public static string? ReadRefreshToken(HttpContext http, SignInSessionOptions options, string? fromBody)
    {
        if (options.AcceptRefreshTokenInBody && !string.IsNullOrWhiteSpace(fromBody))
        {
            return fromBody;
        }

        return ReadCookie(http, options);
    }

    /// <summary>The cookie's token if the request is allowed to use it (CSRF header present), otherwise null.</summary>
    public static string? ReadCookie(HttpContext http, SignInSessionOptions options)
    {
        if (!http.Request.Cookies.TryGetValue(options.RefreshCookieName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return http.Request.Headers.ContainsKey(CsrfHeader) ? value : null;
    }

    /// <summary>Puts the refresh token in the cookie and takes it out of the body.</summary>
    public static AuthResponse Attach(HttpContext http, SignInSessionOptions options, AuthResponse response, DateTimeOffset refreshExpiresAtUtc)
    {
        http.Response.Cookies.Append(options.RefreshCookieName, response.RefreshToken, Options(http, refreshExpiresAtUtc));
        return response with { RefreshToken = string.Empty };
    }

    public static void Clear(HttpContext http, SignInSessionOptions options) =>
        http.Response.Cookies.Delete(options.RefreshCookieName, Options(http, null));

    private static CookieOptions Options(HttpContext http, DateTimeOffset? expiresAtUtc)
    {
        var environment = http.RequestServices.GetService(typeof(Microsoft.Extensions.Hosting.IHostEnvironment)) as Microsoft.Extensions.Hosting.IHostEnvironment;
        return new CookieOptions
        {
            HttpOnly = true,
            // Always Secure outside local development (the proxy's forwarded scheme is honoured
            // by UseForwardedHeaders); in development plain http://localhost works too.
            Secure = environment is null || !environment.IsDevelopment() || http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expiresAtUtc,
            IsEssential = true
        };
    }
}
