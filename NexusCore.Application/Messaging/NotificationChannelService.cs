using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

/// <summary>
/// The SMS panel settings, stored as one tenant-scoped platform setting (platform.Settings,
/// key <see cref="NotificationChannelSettingsReader.SettingKey"/>) - the same table and repository every other platform
/// setting uses. Credentials are encrypted with ASP.NET Core Data Protection before they are
/// written and are never sent back to a client.
/// </summary>
public sealed class NotificationChannelService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    IDataProtectionProvider dataProtectionProvider,
    INotificationChannelSettingsReader reader,
    IEnumerable<ISmsProvider> providers,
    ISmsSender smsSender,
    IOutboundAddressPolicy outboundAddresses) : INotificationChannelService
{
    private const int MaxStoredLength = 2000; // platform.Settings.Value column size

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(NotificationChannelSettingsReader.ProtectorPurpose);

    public IReadOnlyList<SmsProviderDto> ListProviders() =>
        providers.Select(provider => new SmsProviderDto(provider.Key, provider.DisplayName, provider.DefaultBaseUrl)).ToList();

    public async Task<Result<NotificationChannelSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<NotificationChannelSettingsDto>(Error.Unauthorized());
        }

        return Result.Success(Mask(await reader.ReadAsync(tenantId, cancellationToken)));
    }

    public async Task<Result<NotificationChannelSettingsDto>> SaveAsync(
        NotificationChannelSettingsDto settings, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<NotificationChannelSettingsDto>(Error.Unauthorized());
        }

        var current = await reader.ReadAsync(tenantId, cancellationToken);
        // An empty key means "keep the stored one" - the client never had it to send back.
        // ...unless the address the key is sent to changes in the same save: then the stored key
        // is dropped, and the new address only ever receives a key typed in for it. Otherwise
        // editing the address alone would hand the stored key to the new owner of that address.
        var addressChanges = !SameAddress(settings.Sms?.ApiUrl, current.Sms.ApiUrl);
        var apiKey = string.IsNullOrWhiteSpace(settings.Sms?.ApiKey) ? (addressChanges ? null : current.Sms.ApiKey) : settings.Sms.ApiKey.Trim();
        var username = string.IsNullOrWhiteSpace(settings.Sms?.Username) ? (addressChanges ? null : current.Sms.Username) : settings.Sms.Username.Trim();
        var password = string.IsNullOrWhiteSpace(settings.Sms?.Password) ? (addressChanges ? null : current.Sms.Password) : settings.Sms.Password.Trim();
        var configuredProviders = BuildStoredProviders(settings.Sms?.Providers, current.Sms);

        var validation = await ValidateAsync(settings, apiKey, username, password, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<NotificationChannelSettingsDto>(validation.Error);
        }

        var stored = new NotificationChannelSettingsReader.StoredSettings(
            new NotificationChannelSettingsReader.StoredSms(
                settings.Sms!.Enabled,
                settings.Sms.Provider.Trim().ToLowerInvariant(),
                Protect(apiKey),
                Clean(settings.Sms.LineNumber),
                Clean(settings.Sms.ApiUrl),
                Protect(username),
                Protect(password),
                configuredProviders));

        var value = JsonSerializer.Serialize(stored, NotificationChannelSettingsReader.Json);
        if (value.Length > MaxStoredLength)
        {
            return Result.Failure<NotificationChannelSettingsDto>(
                Error.Validation("The notification settings are too long to store."));
        }

        var setting = await repository.FindSettingAsync(tenantId, NotificationChannelSettingsReader.SettingKey, NotificationChannelSettingsReader.SettingScope, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting(Guid.NewGuid(), tenantId, NotificationChannelSettingsReader.SettingKey, value, NotificationChannelSettingsReader.SettingScope);
            await repository.AddSettingAsync(setting, cancellationToken);
        }
        else
        {
            setting.UpdateValue(value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // The audit entry names the change only; it never contains the secret.
        await platformService.AuditAsync("settings.sms_panel", nameof(SystemSetting), setting.Id.ToString(), NotificationChannelSettingsReader.SettingKey, cancellationToken);

        return Result.Success(Mask(await reader.ReadAsync(tenantId, cancellationToken)));
    }

    public async Task<Result<ChannelTestResultDto>> TestSmsAsync(TestSmsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<ChannelTestResultDto>(Error.Unauthorized());
        }

        var phone = Domain.Identity.PhoneNumber.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return Result.Failure<ChannelTestResultDto>(Error.Validation("Enter a valid mobile number for the test message."));
        }

        var text = string.IsNullOrWhiteSpace(request.Message)
            ? "🔔 پیامک آزمایشی سامانه مدیریت فعالیت‌ها"
            : request.Message.Trim();
        if (text.Length > 500)
        {
            return Result.Failure<ChannelTestResultDto>(Error.Validation("The test message is too long (500 characters at most)."));
        }

        var sent = await smsSender.SendAsync(tenantId, phone, text, cancellationToken);
        await platformService.AuditAsync(sent.IsSuccess ? "settings.sms_test_sent" : "settings.sms_test_failed", nameof(SystemSetting), null, phone, cancellationToken);
        return Result.Success(sent.IsSuccess
            ? new ChannelTestResultDto(true, $"پیامک آزمایشی به شماره {phone} ارسال شد. (شناسه پیام: {sent.Value})")
            : new ChannelTestResultDto(false, sent.Error.Message));
    }

    private async Task<Result> ValidateAsync(NotificationChannelSettingsDto settings, string? apiKey, string? username, string? password, CancellationToken cancellationToken)
    {
        if (settings.Sms is null)
        {
            return Result.Failure(Error.Validation("The sms section is required."));
        }

        var provider = settings.Sms.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!providers.Any(p => p.Key == provider))
        {
            return Result.Failure(Error.Validation(
                $"Unknown SMS provider '{settings.Sms.Provider}'. Available: {string.Join(", ", providers.Select(p => p.Key))}."));
        }

        if (!string.IsNullOrWhiteSpace(username) != !string.IsNullOrWhiteSpace(password))
        {
            return Result.Failure(Error.Validation("SMS username and password must be entered together."));
        }

        if (settings.Sms.Enabled && string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure(Error.Validation("An enabled SMS panel needs an API key or username and password."));
        }

        if (settings.Sms.LineNumber is { Length: > 0 } line && (line.Trim().Length > 20 || !line.Trim().All(char.IsAsciiDigit)))
        {
            return Result.Failure(Error.Validation("The sender number must be digits only (20 at most)."));
        }

        var defaultUrls = providers.Select(p => p.DefaultBaseUrl).ToList();
        var urls = new List<string?> { settings.Sms.ApiUrl };
        urls.AddRange(settings.Sms.Providers?.Select(provider => provider.ApiUrl) ?? []);
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out _))
            {
                return Result.Failure(Error.Validation($"'{url}' is not a valid URL."));
            }

            // Where the stored key is sent to: the provider's own address, or a host the
            // operator of this server listed - never an address typed into a settings screen.
            var allowed = await outboundAddresses.ValidateGatewayUrlAsync(url, defaultUrls, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed;
            }
        }

        return Result.Success();
    }

    /// <summary>What a client may see: configuration flags but no credential values.</summary>
    private static NotificationChannelSettingsDto Mask(NotificationChannelSettingsDto settings) =>
        new(settings.Sms with
        {
            ApiKey = null,
            Username = null,
            Password = null,
            Providers = settings.Sms.Providers?.Select(provider => provider with
            {
                ApiKey = null,
                Username = null,
                Password = null
            }).ToList()
        });

    private string? Protect(string? secret) =>
        string.IsNullOrWhiteSpace(secret) ? null : _protector.Protect(secret.Trim());

    private List<NotificationChannelSettingsReader.StoredSmsProvider>? BuildStoredProviders(
        IReadOnlyList<SmsProviderConfigurationDto>? requested,
        SmsChannelSettingsDto current)
    {
        if (requested is null)
        {
            return null;
        }

        return requested.Select(provider =>
        {
            var existing = current.Providers?.FirstOrDefault(item => item.Id == provider.Id);
            var legacyProvider = current.Providers is null && provider.Id == Guid.Parse("00000000-0000-0000-0000-000000000001")
                && provider.Provider.Equals(current.Provider, StringComparison.OrdinalIgnoreCase);
            // A stored credential is kept only while the address it is sent to stays the same.
            var previousUrl = existing is not null ? existing.ApiUrl : legacyProvider ? current.ApiUrl : null;
            var keep = (existing is not null || legacyProvider) && SameAddress(provider.ApiUrl, previousUrl);
            var apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? (keep ? existing?.ApiKey ?? (legacyProvider ? current.ApiKey : null) : null) : provider.ApiKey;
            var username = string.IsNullOrWhiteSpace(provider.Username) ? (keep ? existing?.Username ?? (legacyProvider ? current.Username : null) : null) : provider.Username;
            var password = string.IsNullOrWhiteSpace(provider.Password) ? (keep ? existing?.Password ?? (legacyProvider ? current.Password : null) : null) : provider.Password;
            return new NotificationChannelSettingsReader.StoredSmsProvider(
                provider.Id == Guid.Empty ? Guid.NewGuid() : provider.Id,
                Clean(provider.Name) ?? "سرویس‌دهنده پیامک",
                Clean(provider.Provider)?.ToLowerInvariant() ?? NotificationChannelSettingsReader.KavenegarKey,
                provider.Enabled,
                Clean(provider.ApiUrl),
                Protect(apiKey),
                Clean(provider.LineNumber),
                Protect(username),
                Protect(password));
        }).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Whether two gateway addresses are the same one (empty = the provider's default).</summary>
    private static bool SameAddress(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim().TrimEnd('/'), (b ?? string.Empty).Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
