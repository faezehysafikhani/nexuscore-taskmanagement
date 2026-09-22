using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Platform.Interfaces;

namespace NexusCore.Application.Messaging;

/// <summary>
/// Reads and decrypts a tenant's SMS gateway settings from platform.Settings.
/// Separate from <see cref="NotificationChannelService"/> because the senders need the
/// settings and the service needs the senders (for its test actions).
/// </summary>
public sealed class NotificationChannelSettingsReader(
    IPlatformRepository repository,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<NotificationChannelSettingsReader> logger) : INotificationChannelSettingsReader
{
    public const string SettingKey = "Notifications.Channels";
    public const string SettingScope = "Integrations";
    public const string ProtectorPurpose = "NexusCore.Messaging.ChannelSecrets.v1";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public static NotificationChannelSettingsDto Defaults { get; } = new(
        new SmsChannelSettingsDto(false, SmsProviders.Kavenegar, null, null, null, null));

    public async Task<NotificationChannelSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            return Defaults;
        }

        StoredSettings? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredSettings>(setting.Value, Json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Notification channel settings for tenant {TenantId} are unreadable; using defaults.", tenantId);
            return Defaults;
        }

        if (stored is null)
        {
            return Defaults;
        }

        return new NotificationChannelSettingsDto(
            new SmsChannelSettingsDto(
                stored.Sms?.Enabled ?? false,
                stored.Sms?.Provider ?? SmsProviders.Kavenegar,
                Unprotect(stored.Sms?.ApiKey, tenantId),
                stored.Sms?.LineNumber,
                stored.Sms?.PatternCode,
                stored.Sms?.ApiUrl));
    }

    private string? Unprotect(string? protectedSecret, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            // The Data Protection key ring changed (new machine, lost keys). The secret cannot be
            // recovered; the admin has to enter it again.
            logger.LogWarning("A notification channel secret for tenant {TenantId} could not be decrypted and must be re-entered.", tenantId);
            return null;
        }
    }

    internal sealed record StoredSms(bool Enabled, string Provider, string? ApiKey, string? LineNumber, string? PatternCode, string? ApiUrl);

    internal sealed record StoredSettings(StoredSms? Sms);
}
