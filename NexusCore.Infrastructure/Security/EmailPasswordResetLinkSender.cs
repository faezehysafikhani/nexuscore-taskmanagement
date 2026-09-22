using Microsoft.Extensions.Options;
using NexusCore.Application.Messaging;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// Emails the reset link through <see cref="IEmailSender"/> (Email:Smtp). Unlike
/// <see cref="LoggingPasswordResetLinkSender"/> it never writes the token to the log.
/// </summary>
public sealed class EmailPasswordResetLinkSender(
    IOptions<PasswordResetOptions> options,
    IEmailSender emailSender) : IPasswordResetLinkSender
{
    public TimeSpan TokenLifetime => TimeSpan.FromMinutes(Math.Max(1, options.Value.TokenLifetimeMinutes));

    public bool ExposeTokenInResponse => options.Value.ReturnTokenInResponse;

    public bool IsAvailable => emailSender.IsConfigured;

    public async Task<Result<string>> SendAsync(User user, string rawToken, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var template = options.Value.ResetUrlTemplate.TrimEnd('/');
        var separator = template.Contains('?') ? "&" : "?";
        var resetLink = $"{template}{separator}token={Uri.EscapeDataString(rawToken)}";

        if (!emailSender.IsConfigured)
        {
            return Result.Failure<string>(Error.Validation(
                "Password reset by email is not available: email delivery is not configured on the server."));
        }

        // Email is optional; an account without one has nowhere to receive the link.
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return Result.Failure<string>(Error.Validation("This account has no email address to send a reset link to."));
        }

        var minutes = (int)Math.Ceiling((expiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes);
        var body =
            $"سلام {user.DisplayName}،\n\n" +
            $"برای تعیین رمز عبور جدید روی پیوند زیر کلیک کنید:\n\n{resetLink}\n\n" +
            $"این پیوند تا {minutes} دقیقه معتبر است و فقط یک بار قابل استفاده است.\n" +
            "اگر این درخواست را شما نداده‌اید، این پیام را نادیده بگیرید.";

        var sent = await emailSender.SendAsync(new EmailMessage(user.Email, "بازیابی رمز عبور", body), cancellationToken);
        return sent.IsSuccess ? Result.Success(resetLink) : Result.Failure<string>(sent.Error);
    }
}
