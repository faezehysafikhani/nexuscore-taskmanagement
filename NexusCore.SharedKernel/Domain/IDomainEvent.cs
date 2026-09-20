namespace NexusCore.SharedKernel.Domain;

public interface IDomainEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOnUtc { get; }
}
