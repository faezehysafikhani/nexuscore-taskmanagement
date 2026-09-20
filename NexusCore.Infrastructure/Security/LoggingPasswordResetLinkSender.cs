using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// Default delivery channel. The Core has no e-mail infrastructure, so the reset link is written to
/// the Serilog pipeline. Swap this DI registration for an SMTP sender when mail becomes available.
/// </summary>
public sealed class LoggingPasswordResetLinkSender(
    IOptions<PasswordResetOptions> options,
    ILogger<LoggingPasswordResetLinkSender> logger) : IPasswordResetLinkSender
{
    public TimeSpan TokenLifetime => TimeSpan.FromMinutes(Math.Max(1, options.Value.TokenLifetimeMinutes));

    public bool ExposeTokenInResponse => options.Value.ReturnTokenInResponse;

    public Task<string> SendAsync(User user, string rawToken, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var template = options.Value.ResetUrlTemplate.TrimEnd('/');
        var separator = template.Contains('?') ? "&" : "?";
        var resetLink = $"{template}{separator}token={Uri.EscapeDataString(rawToken)}";

        logger.LogInformation(
            "Password reset link issued for {Email} (expires {ExpiresAtUtc:u}): {ResetLink}",
            user.Email,
            expiresAtUtc,
            resetLink);

        return Task.FromResult(resetLink);
    }
}
