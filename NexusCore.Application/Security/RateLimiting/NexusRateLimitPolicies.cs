namespace NexusCore.Application.Security.RateLimiting;

/// <summary>
/// Shared rate-limit policy names used by NexusCore hosts and reusable modules.
/// </summary>
public static class NexusRateLimitPolicies
{
    public const string Auth = "nexus.auth";
    public const string PasswordRecovery = "nexus.password_recovery";
    public const string PasswordResetVerification = "nexus.password_reset_verification";
    public const string AuthenticatedApi = "nexus.authenticated_api";
    public const string Write = "nexus.write";
    public const string Upload = "nexus.upload";
    public const string ChatSend = "nexus.chat_send";
    public const string Sms = "nexus.sms";
    public const string Realtime = "nexus.realtime";
}
