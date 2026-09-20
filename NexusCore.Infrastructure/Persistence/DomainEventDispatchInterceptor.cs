using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Domain;

namespace NexusCore.Infrastructure.Persistence;

/// <summary>
/// Reusable across every module's DbContext (register it as an interceptor alongside
/// AuditingInterceptor). Collects domain events from tracked entities after a successful
/// SaveChanges, dispatches them, then clears them so they are never re-dispatched.
/// </summary>
public sealed class DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is DbContext context)
        {
            var entitiesWithEvents = context.ChangeTracker.Entries<Entity<Guid>>()
                .Select(entry => entry.Entity)
                .Where(entity => entity.DomainEvents.Count > 0)
                .ToList();

            if (entitiesWithEvents.Count > 0)
            {
                var domainEvents = entitiesWithEvents.SelectMany(entity => entity.DomainEvents).ToList();

                foreach (var entity in entitiesWithEvents)
                {
                    entity.ClearDomainEvents();
                }

                await dispatcher.DispatchAsync(domainEvents, cancellationToken);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
