using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

public sealed record EmailMessage(string To, string Subject, string Body, bool IsHtml = false);

/// <summary>Outgoing email. Configured under Email:Smtp; without it nothing is sent.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Outgoing SMS through the gateway configured in the tenant's notification channel settings.
/// Returns a failure (never throws) so callers can report it or move on.
/// </summary>
public interface ISmsSender
{
    Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string text, CancellationToken cancellationToken);
}

/// <summary>Outgoing Telegram message through the tenant's configured bot.</summary>
public interface ITelegramSender
{
    Task<Result> SendAsync(Guid tenantId, string chatId, string text, CancellationToken cancellationToken);
}

/// <summary>SMS gateways with a real implementation. Anything else is refused, not faked.</summary>
public static class SmsProviders
{
    public const string Kavenegar = "kavenegar";

    public static IReadOnlyList<string> Supported { get; } = [Kavenegar];
}
