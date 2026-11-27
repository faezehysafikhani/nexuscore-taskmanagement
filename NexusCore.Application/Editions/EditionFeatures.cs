using Microsoft.Extensions.Configuration;

namespace NexusCore.Application.Editions;

/// <summary>
/// The features only the commercial edition has. A host turns them all on with
/// <c>Edition:Commercial = true</c>; one feature can still be switched on or off on its own with
/// <c>Edition:Features:&lt;Name&gt;</c> (true/false), which wins over the edition setting. With
/// neither set a feature is off, so a host that knows nothing of editions keeps its old behaviour.
/// </summary>
public static class CommercialFeature
{
    // Identity
    public const string Sso = "Sso";
    public const string Mfa = "Mfa";
    public const string PasswordPolicy = "PasswordPolicy";
    public const string ServiceAccounts = "ServiceAccounts";
    public const string GuestAccounts = "GuestAccounts";
    public const string LdapSync = "LdapSync";
    public const string MoreSmsProviders = "MoreSmsProviders";
    public const string LongerSessions = "LongerSessions";
    public const string Offboarding = "Offboarding";

    // Task management
    public const string CustomStatuses = "CustomStatuses";
    public const string TaskReview = "TaskReview";
    public const string TaskRequests = "TaskRequests";
    public const string Timesheets = "Timesheets";
    public const string TaskTemplates = "TaskTemplates";
    public const string TaskImport = "TaskImport";
    public const string TaskPlanning = "TaskPlanning";
    public const string BulkActions = "BulkActions";
    public const string WorkTypes = "WorkTypes";
    public const string Handoff = "Handoff";

    // Automation, integrations and administration
    public const string Automation = "Automation";
    public const string Sla = "Sla";
    public const string Webhooks = "Webhooks";
    public const string Membership = "Membership";
    public const string ReportSubscriptions = "ReportSubscriptions";
    public const string Retention = "Retention";

    public static readonly IReadOnlyList<string> All =
    [
        Sso, Mfa, PasswordPolicy, ServiceAccounts, GuestAccounts, LdapSync, MoreSmsProviders, LongerSessions, Offboarding,
        CustomStatuses, TaskReview, TaskRequests, Timesheets, TaskTemplates, TaskImport, TaskPlanning, BulkActions, WorkTypes, Handoff,
        Automation, Sla, Webhooks, Membership, ReportSubscriptions, Retention,
    ];
}

/// <summary>Which commercial features this host runs (see <see cref="CommercialFeature"/>).</summary>
public interface IEditionFeatures
{
    bool IsEnabled(string feature);
}

public sealed class EditionFeatures(IConfiguration configuration) : IEditionFeatures
{
    public const string SectionName = "Edition";

    public bool IsEnabled(string feature) => IsEnabled(configuration, feature);

    /// <summary>The same answer straight from configuration (for registration time).</summary>
    public static bool IsEnabled(IConfiguration? configuration, string feature)
    {
        if (configuration is null)
        {
            return false;
        }

        var section = configuration.GetSection(SectionName);
        var own = section.GetSection("Features")[feature];
        if (bool.TryParse(own, out var ownValue))
        {
            return ownValue;
        }

        return bool.TryParse(section["Commercial"], out var commercial) && commercial;
    }
}

/// <summary>Every feature on: for code built without a host (tests that construct services directly).</summary>
public sealed class AllEditionFeatures : IEditionFeatures
{
    public static readonly AllEditionFeatures Instance = new();

    public bool IsEnabled(string feature) => true;
}

public static class EditionFeaturesExtensions
{
    /// <summary>A missing gate means the code runs outside a host: everything on, as before editions.</summary>
    public static bool Allows(this IEditionFeatures? features, string feature) => features?.IsEnabled(feature) ?? true;
}
