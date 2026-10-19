using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum SupplierCategory
{
    Contractor = 0,
    Supplier = 1,
    Consultant = 2,
    Other = 3
}

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum SupplierStatus
{
    Active = 0,
    Inactive = 1
}

/// <summary>
/// The other party of one or more contracts - a contractor, supplier or consultant - kept once for the whole tenant instead of
/// as a name typed into every contract. NormalizedName is the name with look-alike letters, half-spaces, extra spaces and case
/// folded (see <see cref="SupplierNames"/>); it is what keeps two spellings of one company from becoming two suppliers, and it is
/// unique within the tenant.
/// </summary>
public sealed class Supplier : AuditableEntity<Guid>
{
    private Supplier() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public Supplier(Guid id, Guid tenantId, string name, SupplierCategory category) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = SupplierNames.Normalize(name);
        Category = category;
        Status = SupplierStatus.Active;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public SupplierCategory Category { get; private set; }
    public SupplierStatus Status { get; private set; }

    /// <summary>The legal entity's national ID (شناسه ملی) or the person's national code, as typed.</summary>
    public string? NationalId { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Description { get; private set; }

    public void Update(
        string name, SupplierCategory category, SupplierStatus status, string? nationalId, string? contactPerson,
        string? phone, string? email, string? address, string? description)
    {
        Name = name.Trim();
        NormalizedName = SupplierNames.Normalize(name);
        Category = category;
        Status = status;
        NationalId = Clean(nationalId);
        ContactPerson = Clean(contactPerson);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        Description = Clean(description);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The comparison form of a supplier's name. The upgrade script that back-fills suppliers from the contracts' typed names builds the
/// same form in SQL, so the two must stay in step: Arabic yeh and kaf become the Persian letters, a half-space becomes a space,
/// runs of spaces collapse to one, the ends are trimmed, and the result is lower-cased.
/// </summary>
public static class SupplierNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var folded = name.Replace('ي', 'ی').Replace('ك', 'ک').Replace('‌', ' ');
        return System.Text.RegularExpressions.Regex.Replace(folded, @"\s+", " ").Trim().ToLowerInvariant();
    }
}
