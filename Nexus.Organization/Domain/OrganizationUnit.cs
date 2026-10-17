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
        Code = string.Empty;
    }

    public OrganizationUnit(Guid id, Guid tenantId, string name, string code, Guid? parentId = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Code = code.Trim();
        ParentId = parentId;
        IsActive = true;
    }

    /// <summary>
    /// A top-level unit with no code of its own (the shape the flat chart used): the code is derived from the id,
    /// so it is still unique within the tenant.
    /// </summary>
    public OrganizationUnit(Guid id, Guid tenantId, string name, Guid? managerUserId = null)
        : this(id, tenantId, name, $"U{id:N}")
    {
        ManagerUserId = managerUserId;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Renames and re-assigns the unit, keeping its code and its place in the chart.</summary>
    public void Update(string name, Guid? managerUserId, bool isActive) =>
        Update(name, Code, ParentId, managerUserId, isActive);

    public void Update(string name, string code, Guid? parentId, Guid? managerUserId, bool isActive)
    {
        Name = name.Trim();
        Code = code.Trim();
        ParentId = parentId;
        ManagerUserId = managerUserId;
        IsActive = isActive;
    }
}
