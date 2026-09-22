using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Platform.Interfaces;

namespace NexusCore.Application.Messaging;

/// <summary>
/// Reads and decrypts a tenant's SMS gateway settings from platform.Settings - for the senders.
/// The decrypted API key never leaves the server (NotificationChannelService masks it).
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
        new SmsChannelSettingsDto(false, KavenegarKey, null, null, null));

    /// <summary>The provider a panel that was never saved starts on.</summary>
    public const string KavenegarKey = "kavenegar";

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

        // A key that can no longer be decrypted counts as not configured: it has to be entered again.
        var apiKey = Unprotect(stored.Sms?.ApiKey, tenantId);
        return new NotificationChannelSettingsDto(
            new SmsChannelSettingsDto(
                stored.Sms?.Enabled ?? false,
                stored.Sms?.Provider ?? KavenegarKey,
                stored.Sms?.ApiUrl,
                apiKey,
                stored.Sms?.LineNumber,
                apiKey is not null));
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

    internal sealed record StoredSms(bool Enabled, string Provider, string? ApiKey, string? LineNumber, string? ApiUrl);

    internal sealed record StoredSettings(StoredSms? Sms);
}
