using NexusCore.SharedKernel.Domain;

namespace Nexus.OrgStructure.Domain;

/// <summary>
/// A person in a position node of the simple chart. A position may have several people, one person
/// may be in several positions. <see cref="IsPrimary"/>: the official one of the position - whose
/// manager role counts when the position is a unit's head, or has positions under it.
/// </summary>
public sealed class NodePerson : AuditableEntity<Guid>
{
    private NodePerson() : base(Guid.Empty)
    {
    }

    public NodePerson(Guid id, Guid tenantId, Guid unitId, Guid userId, bool isPrimary, DateTimeOffset addedAtUtc, DateTimeOffset? startDate = null) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        UserId = userId;
        IsPrimary = isPrimary;
        AddedAtUtc = addedAtUtc;
        StartDate = startDate;
    }

    public Guid TenantId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }
    public DateTimeOffset? StartDate { get; private set; }

    public void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

/// <summary>A unit's head: one of the positions directly under it. At most one per unit.</summary>
public sealed class NodeHead : AuditableEntity<Guid>
{
    private NodeHead() : base(Guid.Empty)
    {
    }

    public NodeHead(Guid id, Guid tenantId, Guid unitId, Guid headUnitId) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        HeadUnitId = headUnitId;
    }

    public Guid TenantId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid HeadUnitId { get; private set; }

    public void SetHead(Guid headUnitId) => HeadUnitId = headUnitId;
}
