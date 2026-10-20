using NexusCore.SharedKernel.Domain;

namespace Nexus.OrgStructure.Domain;

/// <summary>
/// A company inside a tenant. Several companies can share one tenant (a holding and its
/// subsidiaries): their people can see and work with each other, unlike two tenants.
/// </summary>
public sealed class Company : AuditableEntity<Guid>
{
    private Company() : base(Guid.Empty)
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public Company(Guid id, Guid tenantId, string name, string code, string? description, Guid? managerUserId) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Code = code.Trim();
        Description = Normalize(description);
        ManagerUserId = managerUserId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public string? Description { get; private set; }

    /// <summary>
    /// The company's head (e.g. its CEO): the manager of whoever has no other manager in it. Not
    /// chosen on the company: whoever is placed directly in the company with its position that has
    /// no superior becomes it (see OrgStructureService).
    /// </summary>
    public Guid? ManagerUserId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string code, string? description, bool isActive)
    {
        Name = name.Trim();
        Code = code.Trim();
        Description = Normalize(description);
        IsActive = isActive;
    }

    public void SetHead(Guid? userId) => ManagerUserId = userId;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Puts an organization unit (a deputy, from Nexus.Organization) and everything under it in a
/// company. Only the top unit of a company's branch is linked; its sub-units belong to the same
/// company through their parent.
/// </summary>
public sealed class CompanyUnit : AuditableEntity<Guid>
{
    private CompanyUnit() : base(Guid.Empty)
    {
    }

    public CompanyUnit(Guid id, Guid tenantId, Guid companyId, Guid unitId) : base(id)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        UnitId = unitId;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }

    public void MoveTo(Guid companyId) => CompanyId = companyId;
}

/// <summary>A job title (سِمَت) such as «کارشناس» or «معاون». Not a permission role.</summary>
public sealed class Position : AuditableEntity<Guid>
{
    private Position() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Position(Guid id, Guid tenantId, string title, Guid? parentPositionId = null) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        ParentPositionId = parentPositionId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public Guid? ParentPositionId { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, Guid? parentPositionId, bool isActive)
    {
        Title = title.Trim();
        ParentPositionId = parentPositionId;
        IsActive = isActive;
    }
}

/// <summary>
/// A company a position is for. A position may be for several companies; it is given only to
/// people of those companies and to their deputies. In each company, the one position with no
/// superior is the head's.
/// </summary>
public sealed class PositionCompany : AuditableEntity<Guid>
{
    private PositionCompany() : base(Guid.Empty)
    {
    }

    public PositionCompany(Guid id, Guid tenantId, Guid positionId, Guid companyId) : base(id)
    {
        TenantId = tenantId;
        PositionId = positionId;
        CompanyId = companyId;
    }

    public Guid TenantId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid CompanyId { get; private set; }
}

/// <summary>
/// Where a user sits: their company, optionally their unit (deputy, department, ...) inside it,
/// their position, and optionally a direct manager. Without an explicit manager, the manager is
/// derived from the unit's manager (see HierarchyResolver). One per user per tenant.
/// </summary>
public sealed class UserAssignment : AuditableEntity<Guid>
{
    private UserAssignment() : base(Guid.Empty)
    {
    }

    public UserAssignment(Guid id, Guid tenantId, Guid userId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? UnitId { get; private set; }
    public Guid? PositionId { get; private set; }
    public Guid? ManagerUserId { get; private set; }

    public void Set(Guid companyId, Guid? unitId, Guid? positionId, Guid? managerUserId)
    {
        CompanyId = companyId;
        UnitId = unitId;
        PositionId = positionId;
        ManagerUserId = managerUserId;
    }

}

/// <summary>
/// A post (پُست سازمانی): one place for one person, such as «کارشناس (کد ۷) – معاونت فناوری».
/// In a unit (deputy), in the unit's own order, or directly in the company (UnitId null: e.g. the
/// head's post). Its code is unique in its company. The chart shows it even while vacant. A unit
/// may have several posts of the same position.
/// </summary>
public sealed class UnitPosition : AuditableEntity<Guid>
{
    private UnitPosition() : base(Guid.Empty)
    {
        Code = string.Empty;
    }

    public UnitPosition(Guid id, Guid tenantId, Guid companyId, Guid? unitId, Guid positionId, int sortOrder, string code) : base(id)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        UnitId = unitId;
        PositionId = positionId;
        SortOrder = sortOrder;
        Code = code.Trim();
    }

    public Guid TenantId { get; private set; }

    /// <summary>The company of the post: its unit's, or the one it is directly in.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Null: directly in the company, in no unit.</summary>
    public Guid? UnitId { get; private set; }

    public Guid PositionId { get; private set; }
    public int SortOrder { get; private set; }
    public string Code { get; private set; }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    /// <summary>Its unit moved to another company.</summary>
    public void MoveToCompany(Guid companyId) => CompanyId = companyId;

    public void ChangeCode(string code) => Code = code.Trim();
}

/// <summary>
/// Someone holding a post: one person per post. A person may hold several posts, in any unit;
/// their <see cref="UserAssignment"/> stays their one place in the hierarchy (company, unit, manager).
/// </summary>
public sealed class UserPosition : AuditableEntity<Guid>
{
    private UserPosition() : base(Guid.Empty)
    {
    }

    public UserPosition(Guid id, Guid tenantId, Guid userId, Guid unitPositionId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        UnitPositionId = unitPositionId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid UnitPositionId { get; private set; }
}

/// <summary>A title put before people's names, such as «آقای»، «خانم» or «جناب مهندس».</summary>
public sealed class Honorific : AuditableEntity<Guid>
{
    private Honorific() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Honorific(Guid id, Guid tenantId, string title, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, int sortOrder, bool isActive)
    {
        Title = title.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>Which title a user has. Their display name starts with it (see HonorificService).</summary>
public sealed class UserHonorific : AuditableEntity<Guid>
{
    private UserHonorific() : base(Guid.Empty)
    {
    }

    public UserHonorific(Guid id, Guid tenantId, Guid userId, Guid honorificId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        HonorificId = honorificId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid HonorificId { get; private set; }

    public void Change(Guid honorificId) => HonorificId = honorificId;
}
