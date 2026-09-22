using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Security;

/// <summary>Anonymous account actions that are throttled per client.</summary>
public enum AuthAction
{
    Login,
    Captcha,
    ForgotPassword
}

/// <summary>
/// Server-side protection of the anonymous account endpoints: the CAPTCHA that sign-in asks for
/// after a failed attempt, and the attempt limits.
///
/// All state lives on the server (keyed by client address and by sign-in identifier), so
/// reloading the page, dropping a cookie or calling the API directly does not reset it. A
/// CAPTCHA belongs to the client that requested it and is consumed by the first attempt to
/// answer it, right or wrong.
/// </summary>
public interface ILoginProtection
{
    /// <summary>A new CAPTCHA for the calling client. The answer is never returned.</summary>
    Task<Result<CaptchaChallengeDto>> IssueCaptchaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs before the credentials are looked at. Fails with too_many_requests when the client
    /// or the identifier is over its limit, captcha.required when a CAPTCHA is due but was not
    /// sent, and captcha.invalid when it was answered wrongly, expired or belongs to someone else.
    /// </summary>
    Task<Result> BeforeLoginAttemptAsync(string identifier, string? captchaId, string? captchaAnswer, CancellationToken cancellationToken);

    /// <summary>Records a failed sign-in. Returns whether the next attempt needs a CAPTCHA.</summary>
    Task<bool> RecordFailedLoginAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Clears the failure state of the identifier and the client after a successful sign-in.</summary>
    Task RecordSuccessfulLoginAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Counts one use of an anonymous action by the calling client; too_many_requests when over the limit.</summary>
    Task<Result> ThrottleAsync(AuthAction action, CancellationToken cancellationToken);
}

/// <summary>Identity:LoginProtection. The defaults are the intended production behaviour.</summary>
public sealed class LoginProtectionOptions
{
    public const string SectionName = "Identity:LoginProtection";

    /// <summary>Failed sign-ins (by identifier or by client) after which a CAPTCHA is required. 1 = from the second attempt on.</summary>
    public int CaptchaAfterFailedAttempts { get; set; } = 1;

    /// <summary>How long failed sign-ins are remembered.</summary>
    public int FailureWindowMinutes { get; set; } = 30;

    /// <summary>Failed sign-ins for one identifier within the window before it is refused until the window ends.</summary>
    public int MaxFailedAttemptsPerIdentifier { get; set; } = 10;

    public int CaptchaLifetimeSeconds { get; set; } = 120;
    public int CaptchaLength { get; set; } = 5;

    /// <summary>Per-client limits: at most N uses of the action per window.</summary>
    public int MaxLoginAttemptsPerClient { get; set; } = 30;
    public int LoginWindowMinutes { get; set; } = 5;
    public int MaxCaptchasPerClient { get; set; } = 30;
    public int CaptchaWindowMinutes { get; set; } = 5;
    public int MaxPasswordResetRequestsPerClient { get; set; } = 10;
    public int PasswordResetWindowMinutes { get; set; } = 15;
}
