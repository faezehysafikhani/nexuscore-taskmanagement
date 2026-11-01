using NexusCore.Application.Files;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Services;

/// <summary>
/// What the system is called and looks like for everyone, the sign-in page included: its name
/// (shown next to the logo and in the browser tab), its logo and the browser tab's icon. Empty
/// values mean the product's own defaults (the front end keeps them).
/// </summary>
public sealed record BrandingDto(string? SystemName, string? LogoUrl, string? FaviconUrl, string Version);

/// <summary>A branding image to save: its bytes and the name it was uploaded with.</summary>
public sealed record BrandingImage(string FileName, Stream Content, long Length);

public sealed record UpdateBrandingRequest(
    string? SystemName,
    BrandingImage? Logo,
    BrandingImage? Favicon,
    bool RemoveLogo,
    bool RemoveFavicon);

public sealed record BrandingFile(Stream Content, string ContentType, string Version);

public interface IBrandingService
{
    Task<BrandingDto> GetAsync(CancellationToken cancellationToken);

    /// <summary>The logo ("logo") or tab icon ("favicon"); null when none is set.</summary>
    Task<BrandingFile?> OpenImageAsync(string kind, CancellationToken cancellationToken);

    Task<Result<BrandingDto>> UpdateAsync(UpdateBrandingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Kept as system-wide settings (no organization; the generic settings API cannot change them,
/// see PlatformService) and, for the images, in the file storage. Images are PNG, JPEG, WebP or
/// ICO only, checked by their first bytes - never SVG, which could carry a script.
/// </summary>
public sealed class BrandingService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    IPlatformService platform) : IBrandingService
{
    public const string KeyPrefix = "Branding.";
    private const string NameKey = KeyPrefix + "SystemName";
    private const string LogoKey = KeyPrefix + "Logo";
    private const string FaviconKey = KeyPrefix + "Favicon";
    private const string VersionKey = KeyPrefix + "Version";
    private const string Scope = "System";

    public const int MaxNameLength = 60;
    public const int MaxImageBytes = 512 * 1024;

    public async Task<BrandingDto> GetAsync(CancellationToken cancellationToken)
    {
        var name = await ValueAsync(NameKey, cancellationToken);
        var version = await ValueAsync(VersionKey, cancellationToken) ?? "0";
        var logo = await ValueAsync(LogoKey, cancellationToken);
        var favicon = await ValueAsync(FaviconKey, cancellationToken);
        return new BrandingDto(
            string.IsNullOrWhiteSpace(name) ? null : name,
            logo is null ? null : $"/api/platform/branding/logo?v={version}",
            favicon is null ? null : $"/api/platform/branding/favicon?v={version}",
            version);
    }

    public async Task<BrandingFile?> OpenImageAsync(string kind, CancellationToken cancellationToken)
    {
        var key = kind switch { "logo" => LogoKey, "favicon" => FaviconKey, _ => null };
        if (key is null || await ValueAsync(key, cancellationToken) is not { } stored || Parse(stored) is not { } image)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
        return stream is null ? null : new BrandingFile(stream, image.ContentType, await ValueAsync(VersionKey, cancellationToken) ?? "0");
    }

    public async Task<Result<BrandingDto>> UpdateAsync(UpdateBrandingRequest request, CancellationToken cancellationToken)
    {
        var name = request.SystemName?.Trim();
        if (name is { Length: > MaxNameLength })
        {
            return Result.Failure<BrandingDto>(Error.Validation($"The system name can be at most {MaxNameLength} characters."));
        }

        // Read and check both images before anything is saved.
        var logo = await ReadImageAsync(request.Logo, cancellationToken);
        if (logo.IsFailure)
        {
            return Result.Failure<BrandingDto>(logo.Error);
        }

        var favicon = await ReadImageAsync(request.Favicon, cancellationToken);
        if (favicon.IsFailure)
        {
            return Result.Failure<BrandingDto>(favicon.Error);
        }

        var stale = new List<string>();
        if (request.SystemName is not null)
        {
            await SetAsync(NameKey, string.IsNullOrEmpty(name) ? null : name, cancellationToken);
        }

        await ReplaceImageAsync(LogoKey, logo.Value, request.RemoveLogo, stale, cancellationToken);
        await ReplaceImageAsync(FaviconKey, favicon.Value, request.RemoveFavicon, stale, cancellationToken);
        await SetAsync(VersionKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Old images go only once the new settings are saved.
        foreach (var storageKey in stale)
        {
            try
            {
                await storage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file left behind is harmless; the settings no longer point at it.
            }
        }

        await platform.AuditAsync("branding.update", "Branding", null, name, cancellationToken);
        return Result.Success(await GetAsync(cancellationToken));
    }

    private async Task ReplaceImageAsync(string key, (byte[] Bytes, string ContentType, string Extension)? image, bool remove, List<string> stale, CancellationToken cancellationToken)
    {
        if (image is null && !remove)
        {
            return;
        }

        if (await ValueAsync(key, cancellationToken) is { } previous && Parse(previous) is { } old)
        {
            stale.Add(old.StorageKey);
        }

        if (image is { } fresh)
        {
            using var content = new MemoryStream(fresh.Bytes);
            var saved = await storage.SaveAsync($"branding{fresh.Extension}", fresh.ContentType, content, cancellationToken);
            await SetAsync(key, $"{saved.StorageKey}|{fresh.ContentType}", cancellationToken);
        }
        else
        {
            await SetAsync(key, null, cancellationToken);
        }
    }

    private static async Task<Result<(byte[] Bytes, string ContentType, string Extension)?>> ReadImageAsync(BrandingImage? image, CancellationToken cancellationToken)
    {
        if (image is null)
        {
            return Result.Success<(byte[], string, string)?>(null);
        }

        if (image.Length > MaxImageBytes)
        {
            return Result.Failure<(byte[], string, string)?>(Error.Validation("The image can be at most 512 KB."));
        }

        using var buffer = new MemoryStream();
        await image.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 or > MaxImageBytes)
        {
            return Result.Failure<(byte[], string, string)?>(Error.Validation("The image can be at most 512 KB."));
        }

        var bytes = buffer.ToArray();
        return KindOf(bytes) is { } kind
            ? Result.Success<(byte[], string, string)?>((bytes, kind.ContentType, kind.Extension))
            : Result.Failure<(byte[], string, string)?>(Error.Validation("The image must be PNG, JPEG, WebP or ICO."));
    }

    /// <summary>The image type from the file's first bytes (what it claims to be does not count).</summary>
    private static (string ContentType, string Extension)? KindOf(byte[] head)
    {
        bool Starts(params byte[] magic) => head.Length >= magic.Length && head.AsSpan(0, magic.Length).SequenceEqual(magic);
        if (Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return ("image/png", ".png");
        if (Starts(0xFF, 0xD8, 0xFF)) return ("image/jpeg", ".jpg");
        if (head.Length >= 12 && Starts(0x52, 0x49, 0x46, 0x46) && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return ("image/webp", ".webp");
        if (Starts(0x00, 0x00, 0x01, 0x00)) return ("image/x-icon", ".ico");
        return null;
    }

    private static (string StorageKey, string ContentType)? Parse(string stored)
    {
        var parts = stored.Split('|');
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].StartsWith("image/", StringComparison.Ordinal) ? (parts[0], parts[1]) : null;
    }

    private async Task<string?> ValueAsync(string key, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(null, key, Scope, cancellationToken);
        return string.IsNullOrEmpty(setting?.Value) ? null : setting.Value;
    }

    private async Task SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(null, key, Scope, cancellationToken);
        if (setting is null)
        {
            if (value is not null)
            {
                await repository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), null, key, value, Scope), cancellationToken);
            }
        }
        else
        {
            setting.UpdateValue(value ?? string.Empty);
        }
    }
}
