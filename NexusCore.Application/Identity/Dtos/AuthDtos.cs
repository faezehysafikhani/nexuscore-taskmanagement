namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// Identifier is the sign-in name: a username or a mobile number (any common spelling of it).
/// Email addresses are not sign-in names. CaptchaId/CaptchaAnswer are required once the server
/// asks for a CAPTCHA (after a failed attempt) - see ILoginProtection.
/// </summary>
public sealed record LoginRequest(
    string Identifier,
    string Password,
    string? TenantSlug = null,
    string? CaptchaId = null,
    string? CaptchaAnswer = null);

/// <summary>
/// The refresh token may be left out (null or empty) by a browser that holds it in its HttpOnly
/// cookie; see <c>SessionCookies</c>.
/// </summary>
public sealed record RefreshTokenRequest(string? RefreshToken = null);

/// <summary>
/// RefreshToken is empty when the refresh token was set as a cookie instead of being returned.
/// RefreshTokenExpiresAtUtc is when the sign-in ends for good (the last day of the session).
/// </summary>
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, UserDto User, DateTimeOffset? RefreshTokenExpiresAtUtc = null);
public sealed record CurrentUserResponse(UserDto User, IReadOnlyList<string> Permissions);

/// <summary>
/// A CAPTCHA to solve: an image and the id to send back with the answer. The answer itself is
/// never part of any response.
/// </summary>
public sealed record CaptchaChallengeDto(string CaptchaId, string ImageDataUrl, int ExpiresInSeconds);
