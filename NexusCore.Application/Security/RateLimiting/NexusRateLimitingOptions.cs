namespace NexusCore.Application.Security.RateLimiting;

/// <summary>
/// NexusCore:RateLimiting. Defaults are intentionally production-safe and can be overridden by
/// each product host without changing or rebuilding the reusable packages.
/// </summary>
public sealed class NexusRateLimitingOptions
{
    public const string SectionName = "NexusCore:RateLimiting";

    public bool Enabled { get; set; } = true;

    public RateLimitRule Global { get; set; } = new(600, 1);
    public RateLimitRule Auth { get; set; } = new(10, 1);
    public RateLimitRule PasswordRecovery { get; set; } = new(5, 15);
    public RateLimitRule PasswordResetVerification { get; set; } = new(10, 15);
    public RateLimitRule AuthenticatedApi { get; set; } = new(300, 1);
    public RateLimitRule Write { get; set; } = new(120, 1);
    public RateLimitRule Upload { get; set; } = new(20, 5);
    public RateLimitRule ChatSend { get; set; } = new(60, 1);
    public RateLimitRule Sms { get; set; } = new(5, 5);
    public RateLimitRule Realtime { get; set; } = new(120, 1);
}

public sealed record RateLimitRule(int PermitLimit, int WindowMinutes)
{
    public int PermitLimit { get; init; } = PermitLimit;
    public int WindowMinutes { get; init; } = WindowMinutes;
}
