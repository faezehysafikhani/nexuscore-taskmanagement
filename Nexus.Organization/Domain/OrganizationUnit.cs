using NexusCore.SharedKernel.Domain;

namespace Nexus.Organization.Domain;

/// <summary>
/// Reusable platform capability: consumed optionally by Project, Action, and any future
/// module that needs to scope work to a part of the tenant's org structure. Owned entirely
/// by this module - other modules only ever hold a bare Guid reference, never a navigation
/// property or FK into this table.
/// </summary>
public sealed class OrganizationUnit : AuditableEntity<Guid>
{
    private OrganizationUnit() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public OrganizationUnit(Guid id, Guid tenantId, string name, Guid? managerUserId = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        ManagerUserId = managerUserId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// The unit above this one. Null: directly under the (implicit, row-less) root of its company,
    /// which is how every unit was until now. Kept as a bare reference like every other id here.
    /// </summary>
    public Guid? ParentUnitId { get; private set; }

    /// <summary>Which node type (Nexus.OrgStructure, org_structure.NodeTypes) this unit is; null: the plain «unit».</summary>
    public Guid? NodeTypeId { get; private set; }

    /// <summary>The period the unit exists in (null: always). For planned restructurings; not used yet.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }
    public DateTimeOffset? ValidTo { get; private set; }

    /// <summary>Concurrency token: saving with an older version is refused.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Its place among the nodes beside it (same parent, or the same company's top), lowest first.</summary>
    public int SortOrder { get; private set; }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void SetParent(Guid? parentUnitId) => ParentUnitId = parentUnitId;

    public void SetNodeType(Guid? nodeTypeId) => NodeTypeId = nodeTypeId;

    public void SetValidity(DateTimeOffset? validFrom, DateTimeOffset? validTo)
    {
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public void Update(string name, Guid? managerUserId, bool isActive)
    {
        Name = name.Trim();
        ManagerUserId = managerUserId;
        IsActive = isActive;
    }
}
