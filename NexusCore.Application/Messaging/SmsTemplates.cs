using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    /// <summary>The one-time code of password recovery (Identity).</summary>
    public const string PasswordReset = "password_reset";
}

/// <summary>
/// A template the system knows: its title, the placeholders it offers and its default wording.
/// <paramref name="RequiredPlaceholders"/> must stay in the text (e.g. the code of a password
/// recovery message); <paramref name="Description"/> says when it is sent.
/// </summary>
public sealed record SmsTemplateDefinition(
    string Key,
    string Title,
    IReadOnlyList<string> Placeholders,
    string DefaultText,
    string? Description = null,
    IReadOnlyList<string>? RequiredPlaceholders = null);

/// <summary>
/// The SMS templates one module contributes (like <c>IPermissionCatalog</c> for permissions):
/// each module registers its own, so the templates of a module travel with it.
/// </summary>
public interface ISmsTemplateCatalog
{
    IReadOnlyList<SmsTemplateDefinition> GetTemplates();
}

/// <summary>
/// Notifications:SmsTemplates - how a product uses the templates. <see cref="Keys"/> lists the
/// templates its administrators see and edit (empty = every registered template, the default,
/// so a host that sets nothing behaves as before); hidden ones keep working for whoever sends
/// them. <see cref="ProductName"/> fills the {ProductName} placeholder every template offers.
/// </summary>
public sealed class SmsTemplateOptions
{
    public const string SectionName = "Notifications:SmsTemplates";

    public List<string> Keys { get; set; } = [];

    public string? ProductName { get; set; }
}

/// <summary>The templates of NexusCore itself.</summary>
public sealed class CoreSmsTemplateCatalog : ISmsTemplateCatalog
{
    public IReadOnlyList<SmsTemplateDefinition> GetTemplates() =>
    [
        new(SmsTemplateKeys.PasswordReset, "کد بازیابی رمز عبور", ["Code", "ExpireMinutes"],
            "کد بازیابی رمز عبور شما: {Code}\nاین کد تا {ExpireMinutes} دقیقه معتبر است.",
            "هنگام درخواست بازیابی رمز عبور، به شماره موبایل ثبت‌شده‌ی حساب ارسال می‌شود.",
            ["Code"]),
        new(SmsTemplateKeys.Letter, "متن پیامک نامه", ["recipient", "title", "sender", "date"],
            "{recipient} گرامی، نامه «{title}» از طرف {sender} برای شما ارسال شد."),
        new(SmsTemplateKeys.Referral, "متن پیامک ارجاع", ["recipient", "title", "sender", "date"],
            "{recipient} گرامی، «{title}» توسط {sender} به شما ارجاع شد."),
        new(SmsTemplateKeys.MeetingNotice, "متن پیامک اطلاع‌رسانی جلسه", ["recipient", "title", "date", "time", "location"],
            "{recipient} گرامی، جلسه «{title}» در تاریخ {date} ساعت {time} در {location} برگزار می‌شود."),
        new(SmsTemplateKeys.ProposalRejected, "متن پیامک رد پیشنهاد", ["recipient", "title", "reason"],
            "{recipient} گرامی، پیشنهاد «{title}» رد شد. {reason}"),
    ];
}

public interface ISmsTemplateService
{
    /// <summary>Every template of the caller's tenant: the saved wording, or the default one.</summary>
    Task<Result<IReadOnlyList<SmsTemplateDto>>> ListAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SmsTemplateDto>>> SaveAsync(SaveSmsTemplatesRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The template's text with {placeholders} filled in; unknown placeholders stay empty. A saved
    /// text that lost a required placeholder is not used: the default wording is.
    /// </summary>
    Task<Result<string>> RenderAsync(Guid tenantId, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken);

    /// <summary>Renders the template and sends it through the tenant's SMS panel.</summary>
    Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken);
}

/// <summary>
/// Each template is one row of platform.Settings (key "Notifications.SmsTemplates.&lt;key&gt;"), like
/// every other platform setting. The templates themselves come from the modules' catalogs.
/// </summary>
public sealed partial class SmsTemplateService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    ISmsSender smsSender,
    IEnumerable<ISmsTemplateCatalog> catalogs,
    IOptions<SmsTemplateOptions> options,
    ILogger<SmsTemplateService> logger) : ISmsTemplateService
{
    private const string SettingPrefix = "Notifications.SmsTemplates.";
    private const string SettingScope = "Integrations";
    public const int MaxTextLength = 1000;

    /// <summary>Offered by every template: Notifications:SmsTemplates:ProductName.</summary>
    public const string ProductNamePlaceholder = "ProductName";

    private readonly SmsTemplateOptions _options = options.Value;

    /// <summary>NexusCore's own templates. Kept for existing callers; the full set is <see cref="AllDefinitions"/>.</summary>
    [Obsolete("Templates now come from the registered ISmsTemplateCatalog instances; use AllDefinitions.")]
    public static IReadOnlyList<SmsTemplateDefinition> Definitions { get; } = new CoreSmsTemplateCatalog().GetTemplates();

    /// <summary>Every template the installed modules registered (the first one wins a duplicate key).</summary>
    public IReadOnlyList<SmsTemplateDefinition> AllDefinitions { get; } =
        catalogs.SelectMany(catalog => catalog.GetTemplates()).DistinctBy(definition => definition.Key).ToList();

    /// <summary>The ones this product's administrators see and edit.</summary>
    private IEnumerable<SmsTemplateDefinition> Managed =>
        _options.Keys.Count == 0
            ? AllDefinitions
            : AllDefinitions.Where(definition => _options.Keys.Contains(definition.Key, StringComparer.OrdinalIgnoreCase));

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
            var definition = Managed.FirstOrDefault(d => d.Key == template.Key);
            if (definition is null)
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

            var unknown = UsedPlaceholders(template.Text).Where(name => !Allows(definition, name)).ToList();
            if (unknown.Count > 0)
            {
                return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Validation(
                    $"The text of '{template.Key}' uses unknown placeholders: {string.Join(", ", unknown.Select(name => "{" + name + "}"))}."));
            }

            var missing = MissingRequired(definition, template.Text).ToList();
            if (missing.Count > 0)
            {
                return Result.Failure<IReadOnlyList<SmsTemplateDto>>(Error.Validation(
                    $"The text of '{template.Key}' must contain {string.Join(", ", missing.Select(name => "{" + name + "}"))}."));
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
        var definition = AllDefinitions.FirstOrDefault(d => d.Key == key);
        if (definition is null)
        {
            return Result.Failure<string>(Error.Validation($"Unknown SMS template '{key}'."));
        }

        var setting = await repository.FindSettingAsync(tenantId, SettingPrefix + key, SettingScope, cancellationToken);
        var text = setting?.Value;
        if (string.IsNullOrWhiteSpace(text) || MissingRequired(definition, text).Any())
        {
            if (text is not null)
            {
                // Saved before the rule existed, or edited in the database: never send a message
                // without, say, its code. The values themselves are never logged.
                logger.LogWarning("SMS template {Key} of tenant {TenantId} is incomplete; its default text is used.", key, tenantId);
            }

            text = definition.DefaultText;
        }

        var lookup = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        if (!lookup.ContainsKey(ProductNamePlaceholder))
        {
            lookup[ProductNamePlaceholder] = _options.ProductName;
        }

        var rendered = Placeholder().Replace(text, match =>
            lookup.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty);
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
        foreach (var definition in Managed)
        {
            var setting = await repository.FindSettingAsync(tenantId, SettingPrefix + definition.Key, SettingScope, cancellationToken);
            var placeholders = string.IsNullOrWhiteSpace(_options.ProductName)
                ? definition.Placeholders
                : definition.Placeholders.Append(ProductNamePlaceholder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            list.Add(new SmsTemplateDto(
                definition.Key, definition.Title, setting?.Value ?? definition.DefaultText, placeholders,
                definition.Description, definition.RequiredPlaceholders ?? [], definition.DefaultText));
        }

        return list;
    }

    private static IEnumerable<string> UsedPlaceholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool Allows(SmsTemplateDefinition definition, string placeholder) =>
        string.Equals(placeholder, ProductNamePlaceholder, StringComparison.OrdinalIgnoreCase)
        || definition.Placeholders.Contains(placeholder, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> MissingRequired(SmsTemplateDefinition definition, string text)
    {
        var used = UsedPlaceholders(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (definition.RequiredPlaceholders ?? []).Where(required => !used.Contains(required));
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
