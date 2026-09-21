namespace NexusCore.Application.Messaging;

public sealed record SmsChannelSettingsDto(
    bool Enabled,
    string Provider,
    string? ApiKey,
    string? LineNumber,
    string? PatternCode,
    string? ApiUrl);

public sealed record TelegramChannelSettingsDto(
    bool Enabled,
    string? BotToken,
    string? BotUsername,
    string? AdminChatId,
    string? ApiUrl);

/// <summary>
/// SMS and Telegram gateway settings for the caller's tenant. Secrets (ApiKey, BotToken) are
/// encrypted at rest and only ever returned to holders of settings.view.
/// </summary>
public sealed record NotificationChannelSettingsDto(SmsChannelSettingsDto Sms, TelegramChannelSettingsDto Telegram);

public sealed record TestSmsRequest(string PhoneNumber, string? Message = null);

public sealed record TestTelegramRequest(string ChatId, string? Text = null);

public sealed record ChannelTestResultDto(bool Success, string Message);
