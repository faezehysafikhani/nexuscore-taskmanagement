using NexusCore.SharedKernel.Domain;

namespace Nexus.Organization.Domain;

/// <summary>
/// A person's place in the organisation chart: the one unit they belong to. Held here, as a bare user id, so the
/// identity module stays unaware of the chart (like ManagerUserId on the unit). A user is in at most one unit per tenant.
/// </summary>
public sealed class OrganizationUnitMember : AuditableEntity<Guid>
{
    private OrganizationUnitMember() : base(Guid.Empty)
    {
    }

    public OrganizationUnitMember(Guid id, Guid tenantId, Guid unitId, Guid userId) : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        UserId = userId;
    }

    public Guid TenantId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid UserId { get; private set; }

    public void MoveTo(Guid unitId) => UnitId = unitId;
}
