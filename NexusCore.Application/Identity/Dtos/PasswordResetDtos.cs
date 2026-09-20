namespace NexusCore.Application.Identity.Dtos;

public sealed record ForgotPasswordRequest(string Email, string? TenantSlug);

/// <summary>
/// Deliberately does not reveal whether the account exists (user-enumeration protection).
/// <see cref="ResetToken"/> is populated only when PasswordReset:ReturnTokenInResponse is enabled
/// (development / no-mail-server scenarios).
/// </summary>
public sealed record ForgotPasswordResponse(string Message, string? ResetToken, DateTimeOffset? ExpiresAtUtc);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
