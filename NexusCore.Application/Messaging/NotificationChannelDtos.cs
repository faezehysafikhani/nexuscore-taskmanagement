namespace NexusCore.Application.Messaging;

/// <summary>
/// SMS gateway settings. Credentials are write-only: reads never return their values, and an
/// empty value on save keeps the already stored value.
/// </summary>
public sealed record SmsChannelSettingsDto(
    bool Enabled,
    string Provider,
    string? ApiUrl,
    string? ApiKey,
    string? LineNumber,
    bool ApiKeyConfigured = false,
    string? Username = null,
    string? Password = null,
    bool UsernameConfigured = false,
    bool PasswordConfigured = false,
    IReadOnlyList<SmsProviderConfigurationDto>? Providers = null);

/// <summary>One independently configured SMS provider. Credentials are write-only.</summary>
public sealed record SmsProviderConfigurationDto(
    Guid Id,
    string Name,
    string Provider,
    bool Enabled,
    string? ApiUrl,
    string? ApiKey,
    string? LineNumber,
    string? Username = null,
    string? Password = null,
    bool ApiKeyConfigured = false,
    bool UsernameConfigured = false,
    bool PasswordConfigured = false);

/// <summary>The SMS panel settings of the caller's tenant.</summary>
public sealed record NotificationChannelSettingsDto(SmsChannelSettingsDto Sms);

/// <summary>A provider the panel can be set to.</summary>
public sealed record SmsProviderDto(string Key, string DisplayName, string DefaultBaseUrl);

public sealed record TestSmsRequest(string PhoneNumber, string? Message = null);

public sealed record ChannelTestResultDto(bool Success, string Message);

/// <summary>
/// One SMS text of the system, e.g. the one sent for a referral: its current text, the
/// placeholders it may use (those in RequiredPlaceholders must stay), when it is sent and its
/// default wording.
/// </summary>
public sealed record SmsTemplateDto(
    string Key,
    string Title,
    string Text,
    IReadOnlyList<string> Placeholders,
    string? Description = null,
    IReadOnlyList<string>? RequiredPlaceholders = null,
    string? DefaultText = null);

public sealed record SmsTemplateTextDto(string Key, string Text);

public sealed record SaveSmsTemplatesRequest(IReadOnlyList<SmsTemplateTextDto> Templates);
