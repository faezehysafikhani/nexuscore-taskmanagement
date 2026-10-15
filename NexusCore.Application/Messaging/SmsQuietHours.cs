using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

/// <summary>
/// The hours no notification SMS is sent (e.g. 22:00 to 07:00, overnight). Messages that fall in
/// them wait until they end; codes such as a password reset are never held back.
/// </summary>
public sealed record SmsQuietHoursDto(bool Enabled, string Start, string End);

public sealed class SmsDeliveryOptions
{
    public const string SectionName = "Notifications:Sms";

    /// <summary>The users' clock, for the quiet hours. IANA or Windows id.</summary>
    public string TimeZone { get; set; } = "Asia/Tehran";
}

public interface ISmsQuietHours
{
    /// <summary>The caller's tenant's quiet hours (the defaults until saved).</summary>
    Task<Result<SmsQuietHoursDto>> GetAsync(CancellationToken cancellationToken);

    Task<Result<SmsQuietHoursDto>> SaveAsync(SmsQuietHoursDto request, CancellationToken cancellationToken);

    Task<SmsQuietHoursDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>When the tenant's quiet hours end, if <paramref name="now"/> is in them; null when an SMS may go now.</summary>
    Task<DateTimeOffset?> QuietUntilAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>One row of platform.Settings per tenant, like the rest of the SMS panel.</summary>
public sealed class SmsQuietHours(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    IOptions<SmsDeliveryOptions> options,
    ILogger<SmsQuietHours> logger) : ISmsQuietHours
{
    public const string SettingKey = "Notifications.QuietHours";
    public const string SettingScope = "Integrations";

    /// <summary>Nothing at night unless an administrator changes it.</summary>
    public static SmsQuietHoursDto Defaults { get; } = new(true, "22:00", "07:00");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeZoneInfo _zone = ResolveZone(options.Value.TimeZone);

    public async Task<Result<SmsQuietHoursDto>> GetAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId
            ? Result.Success(await ReadAsync(tenantId, cancellationToken))
            : Result.Failure<SmsQuietHoursDto>(Error.Unauthorized());

    public async Task<Result<SmsQuietHoursDto>> SaveAsync(SmsQuietHoursDto request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<SmsQuietHoursDto>(Error.Unauthorized());
        }

        if (!TryParse(request.Start, out var start) || !TryParse(request.End, out var end))
        {
            return Result.Failure<SmsQuietHoursDto>(Error.Validation("Quiet hours must be times like 22:00."));
        }

        if (start == end)
        {
            return Result.Failure<SmsQuietHoursDto>(Error.Validation("The quiet hours must start and end at different times."));
        }

        var value = new SmsQuietHoursDto(request.Enabled, start.ToString("HH:mm", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture));
        var json = JsonSerializer.Serialize(value, Json);
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            await repository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), tenantId, SettingKey, json, SettingScope), cancellationToken);
        }
        else
        {
            setting.UpdateValue(json);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("settings.sms_quiet_hours", nameof(SystemSetting), null, json, cancellationToken);
        return Result.Success(value);
    }

    public async Task<SmsQuietHoursDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            return Defaults;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<SmsQuietHoursDto>(setting.Value, Json);
            return stored is not null && TryParse(stored.Start, out _) && TryParse(stored.End, out _) ? stored : Defaults;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "SMS quiet hours of tenant {TenantId} are unreadable; the defaults apply.", tenantId);
            return Defaults;
        }
    }

    public async Task<DateTimeOffset?> QuietUntilAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hours = await ReadAsync(tenantId, cancellationToken);
        return hours.Enabled && TryParse(hours.Start, out var start) && TryParse(hours.End, out var end)
            ? QuietUntil(start, end, now, _zone)
            : null;
    }

    /// <summary>When quiet hours from <paramref name="start"/> to <paramref name="end"/> (local) end, if now is in them.</summary>
    public static DateTimeOffset? QuietUntil(TimeOnly start, TimeOnly end, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var today = DateOnly.FromDateTime(local.DateTime);
        DateOnly? endDay = start < end
            ? (time >= start && time < end ? today : null)
            : time >= start ? today.AddDays(1) : time < end ? today : null;
        if (endDay is not { } day)
        {
            return null;
        }

        var endLocal = day.ToDateTime(end);
        return new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal)).ToUniversalTime();
    }

    private static bool TryParse(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static TimeZoneInfo ResolveZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
