using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>
/// The Core SMS sender: reads the tenant's SMS panel settings and hands the message to the
/// provider they name. Knows nothing about any provider's protocol.
/// </summary>
public sealed class GatewaySmsSender(
    INotificationChannelSettingsReader settingsReader,
    IEnumerable<ISmsProvider> providers) : ISmsSender
{
    public async Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        var settings = (await settingsReader.ReadAsync(tenantId, cancellationToken)).Sms;

        if (!settings.Enabled)
        {
            return Result.Failure<string>(Error.Validation("پنل پیامکی در تنظیمات غیرفعال است."));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey)
            && (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password)))
        {
            return Result.Failure<string>(Error.Validation("اطلاعات ورود پنل پیامکی ثبت نشده است."));
        }

        var provider = providers.FirstOrDefault(p => string.Equals(p.Key, settings.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return Result.Failure<string>(Error.Validation($"سرویس‌دهنده پیامک «{settings.Provider}» پشتیبانی نمی‌شود."));
        }

        var receiver = NexusCore.Domain.Identity.PhoneNumber.Normalize(phoneNumber) ?? phoneNumber.Trim();
        return await provider.SendAsync(
            new SmsProviderSettings(settings.ApiUrl ?? provider.DefaultBaseUrl, settings.ApiKey, settings.LineNumber, settings.Username, settings.Password),
            receiver, text, cancellationToken);
    }
}
