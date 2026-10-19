using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Raci.Domain;

/// <summary>What a RACI row is about. The item is a bare Guid: this module does not depend on the modules that own them.</summary>
public enum RaciItemType
{
    WaterfallActivity = 0,
    Deliverable = 1,
    AgileTask = 2
}

public enum RaciRole
{
    Responsible = 0,
    Accountable = 1,
    Consulted = 2,
    Informed = 3
}

/// <summary>One cell of a project's RACI matrix: this person holds this role on this item. A person may hold
/// several roles on the same item (Responsible and Accountable together is common).</summary>
public sealed class RaciAssignment : AuditableEntity<Guid>
{
    private RaciAssignment() : base(Guid.Empty)
    {
    }

    public RaciAssignment(Guid id, Guid tenantId, Guid projectId, RaciItemType itemType, Guid itemId, Guid userId, RaciRole role) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        ItemType = itemType;
        ItemId = itemId;
        UserId = userId;
        Role = role;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public RaciItemType ItemType { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UserId { get; private set; }
    public RaciRole Role { get; private set; }
}
