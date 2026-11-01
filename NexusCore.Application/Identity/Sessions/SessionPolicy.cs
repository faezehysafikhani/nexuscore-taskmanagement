using System.Net;
using System.Text.Json;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Identity.Sessions;

/// <summary>
/// What an organization may configure about sessions, stored as one tenant setting
/// (<see cref="SettingKey"/>). Every value is clamped to the platform's hard range when read, so a
/// stored mistake can never produce a session that lasts for days or ends after a minute.
/// The policy is computed at sign-in and copied onto the session; the strictest profile that
/// applies to the user wins.
/// </summary>
public sealed class SessionPolicySettings
{
    public const string SettingKey = "Identity.Session";
    public const string SettingScope = "Tenant";

    // The platform's hard range. An organization moves inside it, never outside.
    public const int MinIdleMinutes = 5, MaxIdleMinutes = 60;
    public const int MinAbsoluteHours = 1, MaxAbsoluteHours = 12;
    public const int MinConcurrent = 1, MaxConcurrentCeiling = 10;

    /// <summary>Everyone (staff and administrators): 15 minutes idle, 10 hours in all, up to 3 sessions.</summary>
    public int IdleMinutes { get; set; } = 15;
    public int AbsoluteHours { get; set; } = 10;
    public int MaxConcurrent { get; set; } = 3;

    /// <summary>Users who hold a sensitive permission (see <see cref="SessionPolicyProvider.SensitivePermissions"/>).</summary>
    public int SensitiveIdleMinutes { get; set; } = 10;
    public int SensitiveAbsoluteHours { get; set; } = 8;
    public int SensitiveMaxConcurrent { get; set; } = 2;

    /// <summary>The built-in system administrator.</summary>
    public int SystemIdleMinutes { get; set; } = 10;
    public int SystemAbsoluteHours { get; set; } = 8;
    public int SystemMaxConcurrent { get; set; } = 1;

    /// <summary>
    /// The bank's internal address ranges (CIDR, e.g. "10.0.0.0/8"). When empty nothing is "outside":
    /// the profile below never applies. When set, a sign-in from any other address gets it.
    /// </summary>
    public List<string> InternalNetworks { get; set; } = [];
    public int ExternalIdleMinutes { get; set; } = 10;
    public int ExternalAbsoluteHours { get; set; } = 4;

    public SessionPolicySettings Clamped()
    {
        return new SessionPolicySettings
        {
            IdleMinutes = Math.Clamp(IdleMinutes, MinIdleMinutes, MaxIdleMinutes),
            AbsoluteHours = Math.Clamp(AbsoluteHours, MinAbsoluteHours, MaxAbsoluteHours),
            MaxConcurrent = Math.Clamp(MaxConcurrent, MinConcurrent, MaxConcurrentCeiling),
            SensitiveIdleMinutes = Math.Clamp(SensitiveIdleMinutes, MinIdleMinutes, MaxIdleMinutes),
            SensitiveAbsoluteHours = Math.Clamp(SensitiveAbsoluteHours, MinAbsoluteHours, MaxAbsoluteHours),
            SensitiveMaxConcurrent = Math.Clamp(SensitiveMaxConcurrent, MinConcurrent, MaxConcurrentCeiling),
            SystemIdleMinutes = Math.Clamp(SystemIdleMinutes, MinIdleMinutes, MaxIdleMinutes),
            SystemAbsoluteHours = Math.Clamp(SystemAbsoluteHours, MinAbsoluteHours, MaxAbsoluteHours),
            SystemMaxConcurrent = Math.Clamp(SystemMaxConcurrent, MinConcurrent, MaxConcurrentCeiling),
            InternalNetworks = (InternalNetworks ?? []).Select(range => range.Trim()).Where(range => range.Length > 0).Distinct().ToList(),
            ExternalIdleMinutes = Math.Clamp(ExternalIdleMinutes, MinIdleMinutes, MaxIdleMinutes),
            ExternalAbsoluteHours = Math.Clamp(ExternalAbsoluteHours, MinAbsoluteHours, MaxAbsoluteHours)
        };
    }
}

/// <summary>The limits one session gets.</summary>
public sealed record SessionPolicy(string Profile, int IdleSeconds, int AbsoluteSeconds, int MaxConcurrent, bool OutsideNetwork);

public static class SessionProfiles
{
    public const string Default = "default";
    public const string Sensitive = "sensitive";
    public const string OutsideNetwork = "outside-network";
    public const string System = "system";
}

public interface ISessionPolicyProvider
{
    /// <summary>The limits for a sign-in that is happening now: the strictest profile that applies.</summary>
    Task<SessionPolicy> ResolveAsync(User user, IReadOnlyCollection<string> permissions, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>The organization's stored settings (defaults when none), clamped to the platform range.</summary>
    Task<SessionPolicySettings> GetSettingsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Stores the organization's settings; false when a network range is not valid CIDR.</summary>
    Task<(bool Ok, string? Error)> SaveSettingsAsync(Guid tenantId, SessionPolicySettings settings, CancellationToken cancellationToken);
}

public sealed class SessionPolicyProvider(IPlatformRepository settingsRepository, IUnitOfWork unitOfWork) : ISessionPolicyProvider
{
    /// <summary>
    /// Whoever holds one of these can change who may sign in, what the system sends or what the
    /// records say: taking over such an account affects the whole organization.
    /// </summary>
    public static readonly IReadOnlySet<string> SensitivePermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        IdentityPermissions.UsersCreate, IdentityPermissions.UsersUpdate, IdentityPermissions.UsersDelete,
        IdentityPermissions.UsersAssignRoles, IdentityPermissions.UsersAssignPermissions, IdentityPermissions.UsersChangeStatus,
        IdentityPermissions.RolesCreate, IdentityPermissions.RolesUpdate, IdentityPermissions.RolesAssignPermissions,
        IdentityPermissions.SettingsUpdate, IdentityPermissions.SmsSettingsView, IdentityPermissions.SmsSettingsUpdate,
        IdentityPermissions.LdapSettingsUpdate, IdentityPermissions.AuditLogsView, IdentityPermissions.TenantsManageAll,
        IdentityPermissions.SessionsView, IdentityPermissions.SessionsRevoke, IdentityPermissions.SessionsRevokeAll
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SessionPolicy> ResolveAsync(User user, IReadOnlyCollection<string> permissions, string? ipAddress, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(user.TenantId, cancellationToken);
        var candidates = new List<SessionPolicy>
        {
            new(SessionProfiles.Default, settings.IdleMinutes * 60, settings.AbsoluteHours * 3600, settings.MaxConcurrent, false)
        };

        if (permissions.Any(SensitivePermissions.Contains))
        {
            candidates.Add(new(SessionProfiles.Sensitive, settings.SensitiveIdleMinutes * 60, settings.SensitiveAbsoluteHours * 3600, settings.SensitiveMaxConcurrent, false));
        }

        if (user.IsSystem)
        {
            candidates.Add(new(SessionProfiles.System, settings.SystemIdleMinutes * 60, settings.SystemAbsoluteHours * 3600, settings.SystemMaxConcurrent, false));
        }

        var outside = settings.InternalNetworks.Count > 0 && !IsInside(ipAddress, settings.InternalNetworks);
        if (outside)
        {
            candidates.Add(new(SessionProfiles.OutsideNetwork, settings.ExternalIdleMinutes * 60, settings.ExternalAbsoluteHours * 3600, int.MaxValue, true));
        }

        // The strictest value of each limit among the profiles that apply; the label is the
        // profile that is strictest on the idle timeout (ties: the later, more specific one).
        var label = candidates.OrderBy(c => c.IdleSeconds).ThenByDescending(c => candidates.IndexOf(c)).First().Profile;
        return new SessionPolicy(
            label,
            candidates.Min(c => c.IdleSeconds),
            candidates.Min(c => c.AbsoluteSeconds),
            candidates.Min(c => c.MaxConcurrent),
            outside);
    }

    public async Task<SessionPolicySettings> GetSettingsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var stored = await settingsRepository.FindSettingAsync(tenantId, SessionPolicySettings.SettingKey, SessionPolicySettings.SettingScope, cancellationToken);
        if (stored is null)
        {
            return new SessionPolicySettings().Clamped();
        }

        try
        {
            return (JsonSerializer.Deserialize<SessionPolicySettings>(stored.Value, Json) ?? new SessionPolicySettings()).Clamped();
        }
        catch (JsonException)
        {
            // An unreadable value must not lock everybody out: the defaults apply.
            return new SessionPolicySettings().Clamped();
        }
    }

    public async Task<(bool Ok, string? Error)> SaveSettingsAsync(Guid tenantId, SessionPolicySettings settings, CancellationToken cancellationToken)
    {
        var clamped = settings.Clamped();
        foreach (var range in clamped.InternalNetworks)
        {
            if (!TryParseNetwork(range, out _, out _))
            {
                return (false, $"'{range}' is not a valid address range (use CIDR such as 10.0.0.0/8).");
            }
        }

        var value = JsonSerializer.Serialize(clamped, Json);
        if (value.Length > 1900)
        {
            return (false, "Too many address ranges.");
        }

        var stored = await settingsRepository.FindSettingAsync(tenantId, SessionPolicySettings.SettingKey, SessionPolicySettings.SettingScope, cancellationToken);
        if (stored is null)
        {
            await settingsRepository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), tenantId, SessionPolicySettings.SettingKey, value, SessionPolicySettings.SettingScope), cancellationToken);
        }
        else
        {
            stored.UpdateValue(value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public static bool IsInside(string? ipAddress, IEnumerable<string> networks)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        foreach (var range in networks)
        {
            if (TryParseNetwork(range, out var network, out var prefix) && Contains(network, prefix, ip))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParseNetwork(string range, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;
        var parts = range.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var parsed))
        {
            return false;
        }

        var bits = parsed.GetAddressBytes().Length * 8;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > bits)
            {
                return false;
            }
        }
        else
        {
            prefix = bits;
        }

        network = parsed;
        return true;
    }

    private static bool Contains(IPAddress network, int prefix, IPAddress ip)
    {
        var a = network.GetAddressBytes();
        var b = ip.GetAddressBytes();
        if (a.Length != b.Length)
        {
            return false;
        }

        var fullBytes = prefix / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        var rest = prefix % 8;
        if (rest == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - rest));
        return (a[fullBytes] & mask) == (b[fullBytes] & mask);
    }
}
