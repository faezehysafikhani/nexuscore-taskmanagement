using NexusCore.Application.Platform.Interfaces;

namespace NexusCore.Application.Files;

/// <summary>
/// The system-wide upload rules: which file types are accepted, and the maximum file size - a
/// single number in KB, stored as one more row in platform.Settings (like SMS/LDAP settings),
/// changeable only by an admin (settings.update) through the existing generic settings endpoint.
/// </summary>
public static class UploadPolicySettings
{
    public const string SettingKey = "Uploads.MaxFileSizeKb";
    public const string SettingScope = "System";
    public const int DefaultMaxFileSizeKb = 200;

    /// <summary>
    /// A hard ceiling no admin-configured value may exceed, independent of whatever is stored in
    /// the setting - a sanity backstop against a mistaken or malicious multi-gigabyte value.
    /// </summary>
    public const int HardCeilingKb = 20 * 1024; // 20 MB
}

/// <summary>
/// Excel, Word, PDF and image files - the only types accepted anywhere a user can upload a file
/// in the system. Checked by both extension and content-type, since browsers do not always send
/// an accurate content-type for every file.
/// </summary>
public static class AllowedUploadTypes
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".xlsx", ".xls", ".doc", ".docx", ".pdf",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    ];

    private static readonly HashSet<string> ExtensionSet = new(Extensions, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/pdf",
    };

    public static bool IsAllowed(string? fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(contentType) && ContentTypes.Contains(contentType))
        {
            return true;
        }

        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && ExtensionSet.Contains(extension);
    }
}

public interface IUploadPolicyReader
{
    /// <summary>The current max-upload-size, in KB: the given tenant's own value if one was set,
    /// otherwise the system-wide value (TenantId null - what the admin settings page saves),
    /// otherwise the default; clamped to the hard ceiling.</summary>
    Task<int> GetMaxFileSizeKbAsync(Guid? tenantId, CancellationToken cancellationToken);
}

public sealed class UploadPolicyReader(IPlatformRepository repository) : IUploadPolicyReader
{
    public async Task<int> GetMaxFileSizeKbAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        // FindSettingAsync matches TenantId exactly, so a tenant lookup never sees the system-wide row.
        var setting = tenantId is null
            ? null
            : await repository.FindSettingAsync(tenantId, UploadPolicySettings.SettingKey, UploadPolicySettings.SettingScope, cancellationToken);
        setting ??= await repository.FindSettingAsync(null, UploadPolicySettings.SettingKey, UploadPolicySettings.SettingScope, cancellationToken);

        if (setting is null || !int.TryParse(setting.Value, out var configuredKb) || configuredKb <= 0)
        {
            return UploadPolicySettings.DefaultMaxFileSizeKb;
        }

        return Math.Min(configuredKb, UploadPolicySettings.HardCeilingKb);
    }
}
