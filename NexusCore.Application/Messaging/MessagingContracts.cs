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
/// Outgoing SMS through the gateway configured in the tenant's SMS panel settings. The one entry
/// point every module uses; which provider carries the message is a setting, not the caller's
/// concern. Returns a failure (never throws) so callers can report it or move on.
/// </summary>
public interface ISmsSender
{
    Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string text, CancellationToken cancellationToken);
}

/// <summary>What a provider needs to send one message. Credentials are already decrypted.</summary>
public sealed record SmsProviderSettings(string BaseUrl, string? ApiKey, string? SenderNumber, string? Username = null, string? Password = null);

/// <summary>
/// One SMS gateway (Kavenegar, ...). All provider-specific protocol and error handling lives in
/// its implementation; adding a provider means adding one of these, nothing else.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Stored in the settings, e.g. "kavenegar".</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>The provider's official API address, used when the settings leave it empty.</summary>
    string DefaultBaseUrl { get; }

    /// <summary>
    /// Sends one message; the result is the provider's message id. Failures carry a message
    /// that is safe to show an administrator - never the API key or the request URL.
    /// </summary>
    Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken);
}
