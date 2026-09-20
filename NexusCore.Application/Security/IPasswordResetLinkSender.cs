using NexusCore.Domain.Identity;

namespace NexusCore.Application.Security;

/// <summary>
/// Delivery channel and policy holder for password-reset links.
/// The Core ships with a logging implementation; replace the single DI registration with an SMTP
/// or notification-backed sender without touching the identity flow.
/// Configuration (lifetime, front-end URL) is owned by the implementation so that the Application
/// layer stays free of Infrastructure references.
/// </summary>
public interface IPasswordResetLinkSender
{
    /// <summary>How long an issued reset token stays valid.</summary>
    TimeSpan TokenLifetime { get; }

    /// <summary>
    /// When true the raw token is echoed back by /forgot-password so a front-end can complete the
    /// flow without a mail server. MUST be false in production.
    /// </summary>
    bool ExposeTokenInResponse { get; }

    /// <summary>Delivers the reset link and returns the link that was built.</summary>
    Task<string> SendAsync(User user, string rawToken, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
}
