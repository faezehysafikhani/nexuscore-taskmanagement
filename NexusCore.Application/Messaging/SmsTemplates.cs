using System.Text.RegularExpressions;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

/// <summary>
/// The SMS texts of the system. Modules name a template by key and pass the values for its
/// placeholders; the wording itself is set by an administrator in the SMS panel, never written in
/// a component or an endpoint.
/// </summary>
public static class SmsTemplateKeys
{
    public const string Letter = "letter";
    public const string Referral = "referral";
    public const string MeetingNotice = "meeting_notice";
    public const string ProposalRejected = "proposal_rejected";
}

/// <summary>A template the system knows: its title, the placeholders it offers and its default wording.</summary>
public sealed record SmsTemplateDefinition(string Key, string Title, IReadOnlyList<string> Placeholders, string DefaultText);

public interface ISmsTemplateService
{
    /// <summary>Every template of the caller's tenant: the saved wording, or the default one.</summary>
    Task<Result<IReadOnlyList<SmsTemplateDto>>> ListAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SmsTemplateDto>>> SaveAsync(SaveSmsTemplatesRequest request, CancellationToken cancellationToken);

    /// <summary>The template's text with {placeholders} filled in; unknown placeholders stay empty.</summary>
    Task<Result<string>> RenderAsync(Guid tenantId, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken);

    /// <summary>Renders the template and sends it through the tenant's SMS panel.</summary>
    Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken);
}

/// <summary>
/// Each template is one row of platform.Settings (key "Notifications.SmsTemplates.&lt;key&gt;"), like
/// every other platform setting.
/// </summary>
public sealed partial class SmsTemplateService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    ISmsSender smsSender) : ISmsTemplateService
{
    private const string SettingPrefix = "Notifications.SmsTemplates.";
    private const string SettingScope = "Integrations";
    public const int MaxTextLength = 1000;

    public static IReadOnlyList<SmsTemplateDefinition> Definitions { get; } =
    [
        new(SmsTemplateKeys.Letter, "متن پیامک نامه", ["recipient", "title", "sender", "date"],
            "{recipient} گرامی، نامه «{title}» از طرف {sender} برای شما ارسال شد."),
        new(SmsTemplateKeys.Referral, "متن پیامک ارجاع", ["recipient", "title", "sender", "date"],
            "{recipient} گرامی، «{title}» توسط {sender} به شما ارجاع شد."),
        new(SmsTemplateKeys.MeetingNotice, "متن پیامک اطلاع‌رسانی جلسه", ["recipient", "title", "date", "time", "location"],
            "{recipient} گرامی، جلسه «{title}» در تاریخ {date} ساعت {time} در {location} برگزار می‌شود."),
        new(SmsTemplateKeys.ProposalRejected, "متن پیامک رد پیشنهاد", ["recipient", "title", "reason"],
            "{recipient} گرامی، پیشنهاد «{title}» رد شد. {reason}"),
    ];

    public async Task<Result<IReadOnlyList<SmsTemplateDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Unauthorized());
        }

        return Result.Success(await ReadAllAsync(tenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<SmsTemplateDto>>> SaveAsync(SaveSmsTemplatesRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Unauthorized());
        }

        foreach (var template in request.Templates ?? [])
        {
            if (Definitions.All(definition => definition.Key != template.Key))
            {
                return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Validation($"Unknown SMS template '{template.Key}'."));
            }

            if (string.IsNullOrWhiteSpace(template.Text))
            {
                return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Validation($"The text of '{template.Key}' cannot be empty."));
            }

            if (template.Text.Trim().Length > MaxTextLength)
            {
                return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Validation($"The text of '{template.Key}' is longer than {MaxTextLength} characters."));
            }
        }

        foreach (var template in request.Templates ?? [])
        {
            var key = SettingPrefix + template.Key;
            var setting = await repository.FindSettingAsync(tenantId, key, SettingScope, cancellationToken);
            if (setting is null)
            {
                await repository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), tenantId, key, template.Text.Trim(), SettingScope), cancellationToken);
            }
            else
            {
                setting.UpdateValue(template.Text.Trim());
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("settings.sms_templates", nameof(SystemSetting), null,
            string.Join(",", (request.Templates ?? []).Select(template => template.Key)), cancellationToken);
        return Result.Success(await ReadAllAsync(tenantId, cancellationToken));
    }

    public async Task<Result<string>> RenderAsync(Guid tenantId, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var definition = Definitions.FirstOrDefault(d => d.Key == key);
        if (definition is null)
        {
            return Result.Failure<string>(Error.Validation($"Unknown SMS template '{key}'."));
        }

        var setting = await repository.FindSettingAsync(tenantId, SettingPrefix + key, SettingScope, cancellationToken);
        var text = setting?.Value ?? definition.DefaultText;
        var rendered = Placeholder().Replace(text, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty);
        return Result.Success(rendered.Trim());
    }

    public async Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var text = await RenderAsync(tenantId, key, values, cancellationToken);
        return text.IsFailure
            ? text
            : await smsSender.SendAsync(tenantId, phoneNumber, text.Value!, cancellationToken);
    }

    private async Task<IReadOnlyList<SmsTemplateDto>> ReadAllAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var list = new List<SmsTemplateDto>();
        foreach (var definition in Definitions)
        {
            var setting = await repository.FindSettingAsync(tenantId, SettingPrefix + definition.Key, SettingScope, cancellationToken);
            list.Add(new SmsTemplateDto(definition.Key, definition.Title, setting?.Value ?? definition.DefaultText, definition.Placeholders));
        }

        return list;
    }

    [GeneratedRegex(@"\{([a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
