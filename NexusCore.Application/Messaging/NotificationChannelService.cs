using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

/// <summary>
/// Stores the SMS gateway settings as one tenant-scoped platform setting (platform.Settings,
/// key <see cref="NotificationChannelSettingsReader.SettingKey"/>) - the same table and repository every other platform
/// setting uses. The API key is encrypted with ASP.NET Core Data Protection before it is written.
/// </summary>
public sealed class NotificationChannelService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    IDataProtectionProvider dataProtectionProvider,
    INotificationChannelSettingsReader reader,
    ISmsSender smsSender) : INotificationChannelService
{
    private const int MaxStoredLength = 2000; // platform.Settings.Value column size

    private static readonly string[] KnownProviders = ["kavenegar", "farazsms", "melipayamak", "custom"];

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(NotificationChannelSettingsReader.ProtectorPurpose);

    public async Task<Result<NotificationChannelSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<NotificationChannelSettingsDto>(Error.Unauthorized());
        }

        return Result.Success(await reader.ReadAsync(tenantId, cancellationToken));
    }

    public async Task<Result<NotificationChannelSettingsDto>> SaveAsync(
        NotificationChannelSettingsDto settings, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<NotificationChannelSettingsDto>(Error.Unauthorized());
        }

        var validation = Validate(settings);
        if (validation.IsFailure)
        {
            return Result.Failure<NotificationChannelSettingsDto>(validation.Error);
        }

        var stored = new NotificationChannelSettingsReader.StoredSettings(
            new NotificationChannelSettingsReader.StoredSms(
                settings.Sms.Enabled,
                settings.Sms.Provider.Trim().ToLowerInvariant(),
                Protect(settings.Sms.ApiKey),
                Clean(settings.Sms.LineNumber),
                Clean(settings.Sms.PatternCode),
                Clean(settings.Sms.ApiUrl)));

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
        // The audit entry names the change only; it never contains the secrets.
        await platformService.AuditAsync("settings.notification_channels", nameof(SystemSetting), setting.Id.ToString(), NotificationChannelSettingsReader.SettingKey, cancellationToken);

        return Result.Success(await reader.ReadAsync(tenantId, cancellationToken));
    }

    public async Task<Result<ChannelTestResultDto>> TestSmsAsync(TestSmsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<ChannelTestResultDto>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Result.Failure<ChannelTestResultDto>(Error.Validation("A phone number is required."));
        }

        var text = string.IsNullOrWhiteSpace(request.Message)
            ? "🔔 پیامک آزمایشی سامانه مدیریت فعالیت‌ها"
            : request.Message.Trim();

        var sent = await smsSender.SendAsync(tenantId, request.PhoneNumber.Trim(), text, cancellationToken);
        return Result.Success(sent.IsSuccess
            ? new ChannelTestResultDto(true, $"پیامک آزمایشی به شماره {request.PhoneNumber.Trim()} ارسال شد. (شناسه پیام: {sent.Value})")
            : new ChannelTestResultDto(false, sent.Error.Message));
    }

    private static Result Validate(NotificationChannelSettingsDto settings)
    {
        if (settings.Sms is null)
        {
            return Result.Failure(Error.Validation("The sms section is required."));
        }

        var provider = settings.Sms.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KnownProviders.Contains(provider))
        {
            return Result.Failure(Error.Validation($"Unknown SMS provider '{settings.Sms.Provider}'."));
        }

        // Enabling a gateway that has no implementation would mean messages silently never go
        // out. Refuse it here, where the admin sees the reason.
        if (settings.Sms.Enabled && !SmsProviders.Supported.Contains(provider))
        {
            return Result.Failure(Error.Validation(
                $"Sending SMS through '{provider}' is not implemented. Supported: {string.Join(", ", SmsProviders.Supported)}."));
        }

        if (settings.Sms.Enabled && (string.IsNullOrWhiteSpace(settings.Sms.ApiKey) || string.IsNullOrWhiteSpace(settings.Sms.LineNumber)))
        {
            return Result.Failure(Error.Validation("An enabled SMS gateway needs an API key and a sender line number."));
        }

        var url = settings.Sms.ApiUrl;
        if (!string.IsNullOrWhiteSpace(url)
            && (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            return Result.Failure(Error.Validation($"'{url}' is not a valid http(s) URL."));
        }

        return Result.Success();
    }

    private string? Protect(string? secret) =>
        string.IsNullOrWhiteSpace(secret) ? null : _protector.Protect(secret.Trim());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
