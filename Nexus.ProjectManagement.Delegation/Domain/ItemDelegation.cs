using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Delegation.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum DelegatedItemType
{
    WaterfallActivity = 0,
    AgileTask = 1,
    Action = 2
}

/// <summary>
/// One person handing a piece of work (an activity, a task, an action) to another. It is a record of the hand-over, kept
/// for the trail - handing the item back, or on, ends it (IsActive false) but never deletes it. A chain A to B to C is
/// two records; ending A's ends everything B passed on after it.
/// </summary>
public sealed class ItemDelegation : AuditableEntity<Guid>
{
    private ItemDelegation() : base(Guid.Empty)
    {
    }

    public ItemDelegation(Guid id, Guid tenantId, DelegatedItemType itemType, Guid itemId, Guid? projectId, Guid delegatorUserId, Guid delegateUserId)
        : base(id)
    {
        TenantId = tenantId;
        ItemType = itemType;
        ItemId = itemId;
        ProjectId = projectId;
        DelegatorUserId = delegatorUserId;
        DelegateUserId = delegateUserId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public DelegatedItemType ItemType { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid DelegatorUserId { get; private set; }
    public Guid DelegateUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }

    public void End(DateTimeOffset at)
    {
        IsActive = false;
        EndedAtUtc = at;
    }
}
