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
/// in the system. Decided by the extension and confirmed by the file's first bytes; the content-type
/// a client sends is never trusted.
/// </summary>
public static class AllowedUploadTypes
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".xlsx", ".xls", ".doc", ".docx", ".pdf",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    ];

    private static readonly HashSet<string> ExtensionSet = new(Extensions, StringComparer.OrdinalIgnoreCase);

    // What a file with each accepted extension must look like inside. The extension and the
    // content-type a client sends are only claims; these first bytes are the check, and the
    // content-type that is stored and served comes from the extension, never from the client.
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<string, string> TypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".webm"] = "audio/webm",
        [".ogg"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "audio/mp4",
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
    };

    /// <summary>The content-type a stored file is served with: fixed by its extension.</summary>
    public static string ContentTypeFor(string? fileName)
    {
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName);
        return extension is not null && TypeByExtension.TryGetValue(extension, out var type) ? type : "application/octet-stream";
    }

    /// <summary>
    /// The extension a file is stored under: one of the known ones, in lower case, or ".bin".
    /// What the client typed after the last dot never becomes part of the stored name.
    /// </summary>
    public static string StorageExtensionFor(string? fileName)
    {
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension is not null && TypeByExtension.ContainsKey(extension) ? extension : ".bin";
    }

    private static bool Starts(ReadOnlySpan<byte> head, ReadOnlySpan<byte> magic) => head.StartsWith(magic);

    /// <summary>Whether the first bytes of a file fit its extension (HTML or SVG renamed to .pdf, ...).</summary>
    public static bool HeadMatches(string? fileName, ReadOnlySpan<byte> head)
    {
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => Starts(head, "%PDF"u8),
            ".png" => Starts(head, new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            ".jpg" or ".jpeg" => Starts(head, new byte[] { 0xFF, 0xD8, 0xFF }),
            ".gif" => Starts(head, "GIF8"u8),
            ".bmp" => Starts(head, "BM"u8),
            ".webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".xlsx" or ".docx" => Starts(head, Zip),
            ".xls" or ".doc" => Starts(head, Ole2),
            ".webm" => Starts(head, new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            ".ogg" => Starts(head, "OggS"u8),
            ".wav" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WAVE"u8),
            ".mp3" => Starts(head, "ID3"u8) || (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0),
            ".m4a" or ".mp4" => head.Length >= 8 && head.Slice(4, 4).SequenceEqual("ftyp"u8),
            _ => false,
        };
    }

    /// <summary>Reads the first bytes of a seekable stream, checks them against the extension and puts the stream back.</summary>
    public static async Task<bool> StreamMatchesAsync(string? fileName, Stream content, CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
        {
            return false;
        }

        var start = content.Position;
        var head = new byte[16];
        var read = 0;
        while (read < head.Length)
        {
            var count = await content.ReadAsync(head.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        content.Position = start;
        return HeadMatches(fileName, head.AsSpan(0, read));
    }

    /// <summary>Only the extension decides; a client-sent content-type grants nothing.</summary>
    public static bool IsAllowed(string? fileName, string? contentType)
    {
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && ExtensionSet.Contains(extension);
    }

    /// <summary>Voice recordings sent in chat and comments; still subject to the configured size limit.</summary>
    public static bool IsVoiceMessage(string? fileName, string? contentType)
    {
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName);
        return extension is not null && VoiceExtensions.Contains(extension);
    }

    private static readonly HashSet<string> VoiceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".ogg", ".m4a", ".mp4", ".wav", ".mp3",
    };
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
