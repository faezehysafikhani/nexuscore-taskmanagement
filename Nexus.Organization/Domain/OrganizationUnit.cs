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

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string code, Guid? parentId, Guid? managerUserId, bool isActive)
    {
        Name = name.Trim();
        Code = code.Trim();
        ParentId = parentId;
        ManagerUserId = managerUserId;
        IsActive = isActive;
    }
}
