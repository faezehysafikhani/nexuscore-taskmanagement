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

    /// <summary>The company's head (e.g. its CEO): the manager of whoever has no other manager in it.</summary>
    public Guid? ManagerUserId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string code, string? description, Guid? managerUserId, bool isActive)
    {
        Name = name.Trim();
        Code = code.Trim();
        Description = Normalize(description);
        ManagerUserId = managerUserId;
        IsActive = isActive;
    }

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

    /// <summary>The organizational team (of <see cref="UnitId"/>) the user belongs to, if any.</summary>
    public Guid? TeamId { get; private set; }

    /// <summary>A move to another unit leaves the team behind: a team belongs to one unit.</summary>
    public void Set(Guid companyId, Guid? unitId, Guid? positionId, Guid? managerUserId)
    {
        if (unitId != UnitId)
        {
            TeamId = null;
        }

        CompanyId = companyId;
        UnitId = unitId;
        PositionId = positionId;
        ManagerUserId = managerUserId;
    }

    public void SetTeam(Guid? teamId) => TeamId = teamId;
}

/// <summary>
/// A team inside a unit (deputy), e.g. «تیم فرانت‌اند» of «معاونت فناوری». Defined by an
/// administrator as part of the official structure; each person is in at most one. Not the
/// same as a personal work team (a user group anyone may build for their tasks and chat).
/// </summary>
public sealed class OrgTeam : AuditableEntity<Guid>
{
    private OrgTeam() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public OrgTeam(Guid id, Guid tenantId, Guid unitId, string name, string? description, Guid? leaderUserId) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LeaderUserId = leaderUserId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid UnitId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Shown as the team's lead. It does not change anyone's manager.</summary>
    public Guid? LeaderUserId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? description, Guid? leaderUserId, bool isActive)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LeaderUserId = leaderUserId;
        IsActive = isActive;
    }
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
