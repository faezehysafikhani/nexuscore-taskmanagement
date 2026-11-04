using System.Net.Mail;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>A company's logo to save: its bytes and the name it was uploaded with.</summary>
public sealed record CompanyLogoUpload(string FileName, Stream Content, long Length);

public sealed record CompanyLogoFile(Stream Content, string ContentType);

/// <summary>
/// A company's profile: contact and registration details, what its head is called on the chart
/// and its logo. All optional; the logo is kept in the file storage (PNG, JPEG or WebP, checked by
/// its first bytes - never SVG, which could carry a script).
/// </summary>
public interface IOrgCompanyProfileService
{
    Task<Result<CompanyDto>> SetCompanyDetailsAsync(Guid tenantId, Guid companyId, SetCompanyDetailsRequest request, CancellationToken cancellationToken);

    Task<Result<CompanyDto>> SetCompanyLogoAsync(Guid tenantId, Guid companyId, CompanyLogoUpload logo, CancellationToken cancellationToken);

    Task<Result<CompanyDto>> RemoveCompanyLogoAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken);

    /// <summary>The logo, or null when the company has none (or is not the caller's).</summary>
    Task<CompanyLogoFile?> OpenCompanyLogoAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken);
}

public sealed partial class OrgStructureService : IOrgCompanyProfileService
{
    public const int MaxLogoBytes = 512 * 1024;

    public async Task<Result<CompanyDto>> SetCompanyDetailsAsync(Guid tenantId, Guid companyId, SetCompanyDetailsRequest request, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, companyId, cancellationToken);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        if (DetailsProblem(request) is { } problem)
        {
            return Result.Failure<CompanyDto>(problem);
        }

        Apply(company, request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> SetCompanyLogoAsync(Guid tenantId, Guid companyId, CompanyLogoUpload logo, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, companyId, cancellationToken);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        if (files is null)
        {
            return Result.Failure<CompanyDto>(Error.Validation("ذخیره‌ی لوگو در دسترس نیست."));
        }

        if (logo.Length is <= 0 or > MaxLogoBytes)
        {
            return Result.Failure<CompanyDto>(Error.Validation("لوگو حداکثر ۵۱۲ کیلوبایت می‌تواند باشد."));
        }

        using var buffer = new MemoryStream();
        await logo.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.Length is 0 or > MaxLogoBytes)
        {
            return Result.Failure<CompanyDto>(Error.Validation("لوگو حداکثر ۵۱۲ کیلوبایت می‌تواند باشد."));
        }

        if (ImageKind(bytes) is not { } kind)
        {
            return Result.Failure<CompanyDto>(Error.Validation("لوگو باید تصویر PNG، JPEG یا WebP باشد."));
        }

        using var content = new MemoryStream(bytes);
        var saved = await files.SaveAsync($"company-logo{kind.Extension}", kind.ContentType, content, cancellationToken);
        var old = company.Logo;
        company.SetLogo($"{saved.StorageKey}|{kind.ContentType}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteStoredAsync(old, cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> RemoveCompanyLogoAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, companyId, cancellationToken);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        var old = company.Logo;
        company.SetLogo(null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteStoredAsync(old, cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<CompanyLogoFile?> OpenCompanyLogoAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, companyId, cancellationToken);
        if (files is null || company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken) || ParseLogo(company.Logo) is not { } logo)
        {
            return null;
        }

        var stream = await files.OpenReadAsync(logo.StorageKey, cancellationToken);
        return stream is null ? null : new CompanyLogoFile(stream, logo.ContentType);
    }

    /// <summary>The address of the company's logo; it changes with the logo, so it can be cached.</summary>
    private static string? LogoUrlOf(Company company) =>
        ParseLogo(company.Logo) is { } logo
            ? $"/api/org-structure/companies/{company.Id}/logo?v={Fnv(logo.StorageKey)}"
            : null;

    /// <summary>A short fixed hash of the storage key (the same in every process).</summary>
    private static string Fnv(string value)
    {
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash = unchecked((hash ^ ch) * 16777619u);
        }

        return hash.ToString("x8");
    }

    private static void Apply(Company company, SetCompanyDetailsRequest details) =>
        company.SetDetails(details.Phone, details.Email, details.Website, details.NationalId, details.EconomicCode, details.Address, details.HeadTitle);

    private static Error? DetailsProblem(SetCompanyDetailsRequest details)
    {
        static bool TooLong(string? value, int max) => (value?.Trim().Length ?? 0) > max;
        if (TooLong(details.Phone, 40) || TooLong(details.Email, 200) || TooLong(details.Website, 200)
            || TooLong(details.NationalId, 20) || TooLong(details.EconomicCode, 20) || TooLong(details.Address, 500) || TooLong(details.HeadTitle, 100))
        {
            return Error.Validation("یکی از اطلاعات شرکت بیش از حد بلند است.");
        }

        if (!string.IsNullOrWhiteSpace(details.Email) && !IsEmail(details.Email.Trim()))
        {
            return Error.Validation("ایمیل شرکت درست نیست.");
        }

        if (!AllDigits(details.NationalId) || !AllDigits(details.EconomicCode))
        {
            return Error.Validation("شناسه ملی و کد اقتصادی فقط عدد هستند.");
        }

        return null;
    }

    private static bool IsEmail(string value)
    {
        try
        {
            return new MailAddress(value).Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool AllDigits(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim().All(char.IsDigit);

    /// <summary>The next free automatic code: C-001, C-002, ...</summary>
    private async Task<string> NextCompanyCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var count = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Count;
        for (var n = count + 1; ; n++)
        {
            var code = $"C-{n:000}";
            if (!await repository.CompanyCodeExistsAsync(tenantId, code, null, cancellationToken))
            {
                return code;
            }
        }
    }

    /// <summary>Why the units cannot be linked to a new company, or null. Only top units with no company, people or posts yet.</summary>
    private static Error? NewCompanyUnitsProblem(Snapshot snapshot, IReadOnlyList<Guid> unitIds)
    {
        foreach (var unitId in unitIds.Distinct())
        {
            if (!snapshot.Units.TryGetValue(unitId, out var unit))
            {
                return Error.NotFound("واحد سازمانی پیدا نشد.");
            }

            if (snapshot.Graph.ParentOf(unitId) is not null)
            {
                return Error.Validation($"«{unit.Name}» زیر واحد دیگری است؛ فقط واحدهای بالایی به شرکت وصل می‌شوند.");
            }

            if (snapshot.Links.Any(link => link.UnitId == unitId))
            {
                return Error.Conflict($"«{unit.Name}» به شرکت دیگری وصل است.");
            }

            var subtree = snapshot.Resolver().SubtreeOf(unitId);
            if (snapshot.Assignments.Any(a => a.UnitId is { } u && subtree.Contains(u)) || snapshot.Seats.Any(seat => seat.UnitId is { } u && subtree.Contains(u)))
            {
                return Error.Conflict($"«{unit.Name}» فرد یا پست دارد؛ آن را بعد از ساخت شرکت از «ویرایش شرکت» وصل کنید.");
            }
        }

        return null;
    }

    private async Task LinkNewCompanyUnitsAsync(Guid tenantId, Guid companyId, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken)
    {
        foreach (var unitId in unitIds.Distinct())
        {
            await repository.AddCompanyUnitAsync(new CompanyUnit(Guid.NewGuid(), tenantId, companyId, unitId), cancellationToken);
        }
    }

    private async Task DeleteStoredAsync(string? stored, CancellationToken cancellationToken)
    {
        if (files is null || ParseLogo(stored) is not { } old)
        {
            return;
        }

        try
        {
            await files.DeleteAsync(old.StorageKey, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file left behind is harmless; the company no longer points at it.
        }
    }

    private static (string StorageKey, string ContentType)? ParseLogo(string? stored)
    {
        var parts = stored?.Split('|');
        return parts is { Length: 2 } && parts[0].Length > 0 && parts[1].StartsWith("image/", StringComparison.Ordinal) ? (parts[0], parts[1]) : null;
    }

    private static (string ContentType, string Extension)? ImageKind(byte[] head)
    {
        bool Starts(params byte[] magic) => head.Length >= magic.Length && head.AsSpan(0, magic.Length).SequenceEqual(magic);
        if (Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return ("image/png", ".png");
        if (Starts(0xFF, 0xD8, 0xFF)) return ("image/jpeg", ".jpg");
        if (head.Length >= 12 && Starts(0x52, 0x49, 0x46, 0x46) && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return ("image/webp", ".webp");
        return null;
    }
}
