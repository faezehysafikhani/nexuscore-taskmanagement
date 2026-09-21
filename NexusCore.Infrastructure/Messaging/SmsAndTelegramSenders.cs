using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>
/// Sends SMS through the tenant's configured gateway. Kavenegar is the only gateway with a
/// real implementation (its REST API: GET {base}/{apiKey}/sms/send.json). Any other provider
/// is refused with an explicit error rather than reported as sent.
/// </summary>
public sealed class GatewaySmsSender(
    INotificationChannelSettingsReader settingsReader,
    IHttpClientFactory httpClientFactory,
    ILogger<GatewaySmsSender> logger) : ISmsSender
{
    public const string HttpClientName = "NexusCore.Messaging.Sms";
    private const string KavenegarDefaultBaseUrl = "https://api.kavenegar.com/v1";

    public async Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        var settings = (await settingsReader.ReadAsync(tenantId, cancellationToken)).Sms;

        if (!settings.Enabled)
        {
            return Result.Failure<string>(Error.Validation("SMS delivery is disabled in the notification settings."));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return Result.Failure<string>(Error.Validation("The SMS API key is not set."));
        }

        if (!string.Equals(settings.Provider, SmsProviders.Kavenegar, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(Error.Validation($"Sending SMS through '{settings.Provider}' is not implemented."));
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.ApiUrl) ? KavenegarDefaultBaseUrl : settings.ApiUrl.TrimEnd('/');
        var url = $"{baseUrl}/{Uri.EscapeDataString(settings.ApiKey)}/sms/send.json"
                  + $"?receptor={Uri.EscapeDataString(phoneNumber)}"
                  + $"&sender={Uri.EscapeDataString(settings.LineNumber ?? string.Empty)}"
                  + $"&message={Uri.EscapeDataString(text)}";

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var hasReturn = json.RootElement.TryGetProperty("return", out var ret) && ret.ValueKind == JsonValueKind.Object;
            var status = hasReturn && ret.TryGetProperty("status", out var s) && s.TryGetInt32(out var code)
                ? code
                : (int)response.StatusCode;

            if (status is 200 or 201)
            {
                var messageId = json.RootElement.TryGetProperty("entries", out var entries)
                                && entries.ValueKind == JsonValueKind.Array
                                && entries.GetArrayLength() > 0
                                && entries[0].TryGetProperty("messageid", out var id)
                    ? id.ToString()
                    : "accepted";
                return Result.Success(messageId);
            }

            var reason = hasReturn && ret.TryGetProperty("message", out var m) ? m.GetString() : response.ReasonPhrase;
            logger.LogWarning("Kavenegar rejected an SMS (status {Status}).", status);
            return Result.Failure<string>(Error.Validation($"Kavenegar error {status}: {reason}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The exception message can contain the request URL, and with it the API key.
            logger.LogWarning("SMS gateway request failed: {ErrorType}.", ex.GetType().Name);
            return Result.Failure<string>(Error.Validation("The SMS gateway could not be reached."));
        }
    }
}

/// <summary>Sends Telegram messages with the tenant's bot (Bot API sendMessage).</summary>
public sealed class TelegramBotSender(
    INotificationChannelSettingsReader settingsReader,
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramBotSender> logger) : ITelegramSender
{
    public const string HttpClientName = "NexusCore.Messaging.Telegram";
    private const string DefaultBaseUrl = "https://api.telegram.org";

    public async Task<Result> SendAsync(Guid tenantId, string chatId, string text, CancellationToken cancellationToken)
    {
        var settings = (await settingsReader.ReadAsync(tenantId, cancellationToken)).Telegram;

        if (!settings.Enabled)
        {
            return Result.Failure(Error.Validation("Telegram delivery is disabled in the notification settings."));
        }

        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return Result.Failure(Error.Validation("The Telegram bot token is not set."));
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.ApiUrl) ? DefaultBaseUrl : settings.ApiUrl.TrimEnd('/');

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).PostAsJsonAsync(
                $"{baseUrl}/bot{settings.BotToken}/sendMessage",
                new { chat_id = chatId, text },
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return Result.Success();
            }

            var description = json.RootElement.TryGetProperty("description", out var d) ? d.GetString() : response.ReasonPhrase;
            logger.LogWarning("Telegram rejected a message ({StatusCode}).", (int)response.StatusCode);
            return Result.Failure(Error.Validation($"Telegram: {description}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The request URL contains the bot token, so the exception text is not logged.
            logger.LogWarning("Telegram request failed: {ErrorType}.", ex.GetType().Name);
            return Result.Failure(Error.Validation("The Telegram API could not be reached."));
        }
    }
}
