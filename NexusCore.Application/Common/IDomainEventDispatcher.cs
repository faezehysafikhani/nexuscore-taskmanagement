using NexusCore.SharedKernel.Domain;

namespace NexusCore.Application.Common;

/// <summary>
/// Dispatches domain events raised by entities to whichever IDomainEventHandler&lt;TEvent&gt;
/// implementations are registered. A module that defines no handler for an event type is
/// unaffected - this is how cross-module reactions (e.g. Workflow approving something back to
/// Risk) stay decoupled without either module referencing the other.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
