namespace NexusCore.Application.Identity.Dtos;

/// <summary>Step 1. Identifier is the account's username or mobile number, as at sign-in.</summary>
public sealed record ForgotPasswordRequest(string Identifier, string? TenantSlug);

/// <summary>
/// The same answer whether or not an account matches (user-enumeration protection). The code
/// itself only ever goes to the account's mobile number.
/// </summary>
public sealed record ForgotPasswordResponse(string Message, int CodeLifetimeSeconds);

/// <summary>Step 2. The code from the SMS, with the identifier it was requested for.</summary>
public sealed record VerifyResetCodeRequest(string Identifier, string Code, string? TenantSlug);

/// <summary>A single-use token that allows step 3 (setting the new password) until it expires.</summary>
public sealed record VerifyResetCodeResponse(string ResetToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Step 3. Token is the one returned by the code verification.</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword);
