namespace NexusCore.Application.Identity.Options;

/// <summary>Identity:Mfa - the SMS code after the password.</summary>
public sealed class MfaOptions
{
    public const string SectionName = "Identity:Mfa";

    /// <summary>When true everybody signs in with a code (except the built-in administrator and service accounts); otherwise only those who turned it on.</summary>
    public bool Required { get; set; }

    public int CodeLength { get; set; } = 6;
    public int CodeLifetimeMinutes { get; set; } = 5;
}
