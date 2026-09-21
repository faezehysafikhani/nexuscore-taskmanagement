namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// Email carries the sign-in identifier: an email address, a username or a mobile number.
/// The property keeps its original name so existing clients are unaffected.
/// </summary>
public sealed record LoginRequest(string Email, string Password, string? TenantSlug);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, UserDto User);
public sealed record CurrentUserResponse(UserDto User, IReadOnlyList<string> Permissions);
