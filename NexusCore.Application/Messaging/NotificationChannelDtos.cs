namespace NexusCore.Application.Messaging;

/// <summary>
/// SMS gateway settings. ApiKey is write-only: reads never return it (ApiKeyConfigured says
/// whether one is stored), and a save with an empty ApiKey keeps the stored one.
/// </summary>
public sealed record SmsChannelSettingsDto(
    bool Enabled,
    string Provider,
    string? ApiUrl,
    string? ApiKey,
    string? LineNumber,
    bool ApiKeyConfigured = false);

/// <summary>The SMS panel settings of the caller's tenant.</summary>
public sealed record NotificationChannelSettingsDto(SmsChannelSettingsDto Sms);

/// <summary>A provider the panel can be set to.</summary>
public sealed record SmsProviderDto(string Key, string DisplayName, string DefaultBaseUrl);

public sealed record TestSmsRequest(string PhoneNumber, string? Message = null);

public sealed record ChannelTestResultDto(bool Success, string Message);

/// <summary>One SMS text of the system, e.g. the one sent for a referral.</summary>
public sealed record SmsTemplateDto(string Key, string Title, string Text, IReadOnlyList<string> Placeholders);

public sealed record SmsTemplateTextDto(string Key, string Text);

public sealed record SaveSmsTemplatesRequest(IReadOnlyList<SmsTemplateTextDto> Templates);
