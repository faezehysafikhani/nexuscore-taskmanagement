namespace NexusCore.Application.Identity.Dtos;

public sealed record LoginRequest(string Email, string Password, string? TenantSlug);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, UserDto User);
public sealed record CurrentUserResponse(UserDto User, IReadOnlyList<string> Permissions);
