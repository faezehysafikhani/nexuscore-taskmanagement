namespace NexusCore.SharedKernel.Domain;

public abstract class AuditableEntity<TId> : AggregateRoot<TId>
{
    protected AuditableEntity(TId id) : base(id)
    {
    }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public Guid? ModifiedByUserId { get; set; }
}
