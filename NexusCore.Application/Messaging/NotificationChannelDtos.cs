namespace NexusCore.Application.Messaging;

public sealed record SmsChannelSettingsDto(
    bool Enabled,
    string Provider,
    string? ApiKey,
    string? LineNumber,
    string? PatternCode,
    string? ApiUrl);

/// <summary>
/// The SMS gateway settings of the caller's tenant. The API key is encrypted at rest and only
/// ever returned to holders of settings.view.
/// </summary>
public sealed record NotificationChannelSettingsDto(SmsChannelSettingsDto Sms);

public sealed record TestSmsRequest(string PhoneNumber, string? Message = null);

public sealed record ChannelTestResultDto(bool Success, string Message);
