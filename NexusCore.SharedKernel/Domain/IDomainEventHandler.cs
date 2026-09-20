namespace NexusCore.SharedKernel.Domain;

/// <summary>
/// Implemented by any module that reacts to another module's domain event (e.g. Risk reacting
/// to an approval decision raised by Workflow). Resolved and invoked by the dispatcher after a
/// successful SaveChanges - see NexusCore.Application.Common.IDomainEventDispatcher.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
