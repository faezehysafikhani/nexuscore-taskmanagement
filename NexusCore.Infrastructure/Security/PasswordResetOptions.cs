namespace NexusCore.Infrastructure.Security;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Lifetime of a reset token, in minutes.</summary>
    public int TokenLifetimeMinutes { get; set; } = 30;

    /// <summary>Front-end page that consumes the token. The token is appended as ?token=...</summary>
    public string ResetUrlTemplate { get; set; } = "http://localhost:2400/reset-password";

    /// <summary>
    /// When true the raw token is returned by /forgot-password. Intended for environments with no mail
    /// server. MUST be false in production.
    /// </summary>
    public bool ReturnTokenInResponse { get; set; } = false;
}
