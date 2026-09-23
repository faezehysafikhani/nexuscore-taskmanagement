namespace NexusCore.Application.Identity.Options;

/// <summary>
/// PasswordReset: password recovery by a one-time code sent by SMS to the account's mobile
/// number. Request and attempt limits live with the other sign-in limits
/// (Identity:LoginProtection).
/// </summary>
public sealed class PasswordRecoveryOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Digits in the SMS code.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long an SMS code can be used.</summary>
    public int CodeLifetimeMinutes { get; set; } = 5;

    /// <summary>How long the code, once confirmed, allows setting the new password.</summary>
    public int ResetTokenLifetimeMinutes { get; set; } = 10;
}
