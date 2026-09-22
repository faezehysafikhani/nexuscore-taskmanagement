using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Ldap;

/// <summary>
/// LDAP / Active Directory connection settings of a tenant. BindPassword is write-only: reads
/// never return it (BindPasswordConfigured says whether one is stored) and a save or test with an
/// empty BindPassword uses the stored one.
/// </summary>
public sealed record LdapSettingsDto(
    bool Enabled,
    string? Host,
    int Port,
    bool UseSsl,
    bool UseStartTls,
    string? Domain,
    string? BaseDn,
    string? BindUsername,
    string? BindPassword,
    string? UserSearchBase,
    string? UserFilter,
    int ConnectionTimeoutSeconds,
    bool TrustServerCertificate = false,
    bool BindPasswordConfigured = false);

public sealed record LdapTestResultDto(bool Success, string Message, int? EntriesFound, long ElapsedMilliseconds);

/// <summary>Everything needed to open a connection, with the password decrypted. Server-side only.</summary>
public sealed record LdapConnectionSettings(
    string Host,
    int Port,
    bool UseSsl,
    bool UseStartTls,
    string? Domain,
    string? BaseDn,
    string? BindUsername,
    string? BindPassword,
    string? UserSearchBase,
    string UserFilter,
    int ConnectionTimeoutSeconds,
    bool TrustServerCertificate);

/// <summary>The directory itself: implemented in Infrastructure over System.DirectoryServices.Protocols.</summary>
public interface ILdapDirectoryClient
{
    /// <summary>Connects, binds and runs a small search of the user base. Never throws.</summary>
    Task<LdapTestResultDto> TestConnectionAsync(LdapConnectionSettings settings, CancellationToken cancellationToken);
}

public interface ILdapSettingsService
{
    Task<Result<LdapSettingsDto>> GetAsync(CancellationToken cancellationToken);
    Task<Result<LdapSettingsDto>> SaveAsync(LdapSettingsDto settings, CancellationToken cancellationToken);

    /// <summary>Tests the given settings (or the saved ones when null) without saving them.</summary>
    Task<Result<LdapTestResultDto>> TestAsync(LdapSettingsDto? settings, CancellationToken cancellationToken);
}

/// <summary>
/// Stores the LDAP settings as one tenant-scoped platform setting (key "Identity.Ldap"), like the
/// SMS panel; the bind password is encrypted with ASP.NET Core Data Protection.
/// </summary>
public sealed class LdapSettingsService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    IDataProtectionProvider dataProtectionProvider,
    ILdapDirectoryClient directory,
    ILogger<LdapSettingsService> logger) : ILdapSettingsService
{
    public const string SettingKey = "Identity.Ldap";
    public const string SettingScope = "Integrations";
    public const string DefaultUserFilter = "(&(objectClass=user)(objectCategory=person))";
    private const string ProtectorPurpose = "NexusCore.Ldap.Secrets.v1";
    private const int MaxStoredLength = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    private static readonly LdapSettingsDto Defaults = new(false, null, 389, false, false, null, null, null, null, null, DefaultUserFilter, 10);

    public async Task<Result<LdapSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<LdapSettingsDto>(Error.Unauthorized());
        }

        var (settings, _) = await ReadAsync(tenantId, cancellationToken);
        return Result.Success(settings);
    }

    public async Task<Result<LdapSettingsDto>> SaveAsync(LdapSettingsDto settings, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<LdapSettingsDto>(Error.Unauthorized());
        }

        var (_, storedPassword) = await ReadAsync(tenantId, cancellationToken);
        var password = string.IsNullOrEmpty(settings.BindPassword) ? storedPassword : settings.BindPassword;

        var validation = Validate(settings, requireHost: settings.Enabled);
        if (validation.IsFailure)
        {
            return Result.Failure<LdapSettingsDto>(validation.Error);
        }

        var stored = new Stored(
            settings.Enabled, Clean(settings.Host), settings.Port, settings.UseSsl, settings.UseStartTls,
            Clean(settings.Domain), Clean(settings.BaseDn), Clean(settings.BindUsername),
            string.IsNullOrEmpty(password) ? null : _protector.Protect(password),
            Clean(settings.UserSearchBase), Clean(settings.UserFilter) ?? DefaultUserFilter,
            settings.ConnectionTimeoutSeconds, settings.TrustServerCertificate);

        var value = JsonSerializer.Serialize(stored, Json);
        if (value.Length > MaxStoredLength)
        {
            return Result.Failure<LdapSettingsDto>(Error.Validation("The LDAP settings are too long to store."));
        }

        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting(Guid.NewGuid(), tenantId, SettingKey, value, SettingScope);
            await repository.AddSettingAsync(setting, cancellationToken);
        }
        else
        {
            setting.UpdateValue(value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Names the change only - never the password.
        await platformService.AuditAsync("settings.ldap", nameof(SystemSetting), setting.Id.ToString(), stored.Host, cancellationToken);

        var (saved, _) = await ReadAsync(tenantId, cancellationToken);
        return Result.Success(saved);
    }

    public async Task<Result<LdapTestResultDto>> TestAsync(LdapSettingsDto? settings, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<LdapTestResultDto>(Error.Unauthorized());
        }

        var (saved, storedPassword) = await ReadAsync(tenantId, cancellationToken);
        var candidate = settings ?? saved;
        var validation = Validate(candidate, requireHost: true);
        if (validation.IsFailure)
        {
            return Result.Failure<LdapTestResultDto>(validation.Error);
        }

        var password = string.IsNullOrEmpty(candidate.BindPassword) ? storedPassword : candidate.BindPassword;
        var result = await directory.TestConnectionAsync(new LdapConnectionSettings(
            candidate.Host!.Trim(), candidate.Port, candidate.UseSsl, candidate.UseStartTls,
            Clean(candidate.Domain), Clean(candidate.BaseDn), Clean(candidate.BindUsername), password,
            Clean(candidate.UserSearchBase), Clean(candidate.UserFilter) ?? DefaultUserFilter,
            candidate.ConnectionTimeoutSeconds, candidate.TrustServerCertificate), cancellationToken);

        await platformService.AuditAsync(result.Success ? "settings.ldap_test_succeeded" : "settings.ldap_test_failed",
            nameof(SystemSetting), null, candidate.Host, cancellationToken);
        return Result.Success(result);
    }

    private async Task<(LdapSettingsDto Settings, string? Password)> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            return (Defaults, null);
        }

        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(setting.Value, Json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "LDAP settings for tenant {TenantId} are unreadable; using defaults.", tenantId);
            return (Defaults, null);
        }

        if (stored is null)
        {
            return (Defaults, null);
        }

        string? password = null;
        if (!string.IsNullOrEmpty(stored.BindPassword))
        {
            try
            {
                password = _protector.Unprotect(stored.BindPassword);
            }
            catch (CryptographicException)
            {
                logger.LogWarning("The LDAP bind password of tenant {TenantId} could not be decrypted and must be entered again.", tenantId);
            }
        }

        return (new LdapSettingsDto(
            stored.Enabled, stored.Host, stored.Port, stored.UseSsl, stored.UseStartTls, stored.Domain, stored.BaseDn,
            stored.BindUsername, null, stored.UserSearchBase, stored.UserFilter ?? DefaultUserFilter,
            stored.ConnectionTimeoutSeconds, stored.TrustServerCertificate, password is not null), password);
    }

    private static Result Validate(LdapSettingsDto settings, bool requireHost)
    {
        if (requireHost && string.IsNullOrWhiteSpace(settings.Host))
        {
            return Result.Failure(Error.Validation("The LDAP server (host) is required."));
        }

        if (!string.IsNullOrWhiteSpace(settings.Host) && (settings.Host.Trim().Length > 255 || settings.Host.Contains("://", StringComparison.Ordinal) || settings.Host.Any(char.IsWhiteSpace)))
        {
            return Result.Failure(Error.Validation("Enter the LDAP server as a host name or IP address only (no ldap:// prefix, no spaces)."));
        }

        if (settings.Port is < 1 or > 65535)
        {
            return Result.Failure(Error.Validation("The port must be between 1 and 65535."));
        }

        if (settings.UseSsl && settings.UseStartTls)
        {
            return Result.Failure(Error.Validation("Choose either SSL (LDAPS) or StartTLS, not both."));
        }

        if (settings.ConnectionTimeoutSeconds is < 1 or > 120)
        {
            return Result.Failure(Error.Validation("The connection timeout must be between 1 and 120 seconds."));
        }

        var filter = settings.UserFilter?.Trim();
        if (!string.IsNullOrEmpty(filter) && (!filter.StartsWith('(') || !filter.EndsWith(')') || filter.Count(c => c == '(') != filter.Count(c => c == ')')))
        {
            return Result.Failure(Error.Validation("The user filter must be a valid LDAP filter, e.g. (objectClass=person)."));
        }

        foreach (var (name, value) in new[] { ("Base DN", settings.BaseDn), ("User search base", settings.UserSearchBase) })
        {
            if (!string.IsNullOrWhiteSpace(value) && !value.Contains('='))
            {
                return Result.Failure(Error.Validation($"{name} must be a distinguished name, e.g. DC=example,DC=com."));
            }
        }

        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Stored(
        bool Enabled, string? Host, int Port, bool UseSsl, bool UseStartTls, string? Domain, string? BaseDn,
        string? BindUsername, string? BindPassword, string? UserSearchBase, string? UserFilter,
        int ConnectionTimeoutSeconds, bool TrustServerCertificate);
}
