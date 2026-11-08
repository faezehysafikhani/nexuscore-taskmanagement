using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Domain;

namespace NexusCore.Infrastructure.Persistence;

public class AuditingInterceptor : SaveChangesInterceptor
{
    private const int MaxValueLength = 500;

    private static readonly HashSet<string> AuditColumns =
        ["CreatedAtUtc", "CreatedByUserId", "ModifiedAtUtc", "ModifiedByUserId"];

    private readonly ICurrentUserContext currentUserContext;
    private readonly IReadOnlyList<IEntityChangeObserver> observers;
    private readonly ILogger<AuditingInterceptor>? logger;

    // What each context is about to write, held from SavingChanges until the save succeeds (or fails).
    private readonly ConditionalWeakTable<DbContext, List<EntityChange>> pending = new();

    /// <param name="observers">Optional listeners (see <see cref="IEntityChangeObserver"/>). With none, nothing extra is captured.</param>
    public AuditingInterceptor(
        ICurrentUserContext currentUserContext,
        IEnumerable<IEntityChangeObserver>? observers = null,
        ILogger<AuditingInterceptor>? logger = null)
    {
        this.currentUserContext = currentUserContext;
        this.observers = observers?.ToList() ?? [];
        this.logger = logger;
    }


    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;

        if (context == null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);


        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity<Guid> entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAtUtc = now;
                    entity.CreatedByUserId = currentUserContext.UserId;
                }

                if (entry.State == EntityState.Modified)
                {
                    entity.ModifiedAtUtc = now;
                    entity.ModifiedByUserId = currentUserContext.UserId;
                }
            }
        }

        if (observers.Count > 0)
        {
            pending.Remove(context);
            var changes = Capture(context, now);
            if (changes.Count > 0)
            {
                pending.Add(context, changes);
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && pending.TryGetValue(context, out var changes))
        {
            // Taken out first: an observer (or a domain-event handler after this) may save again.
            pending.Remove(context);
            foreach (var observer in observers)
            {
                try
                {
                    await observer.OnChangesSavedAsync(changes, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The business save has already been committed; never turn that into a failure.
                    logger?.LogError(exception, "Entity change observer {Observer} failed.", observer.GetType().Name);
                }
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            pending.Remove(context);
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private List<EntityChange> Capture(DbContext context, DateTimeOffset now)
    {
        var changes = new List<EntityChange>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || entry.Entity is not Entity<Guid> entity)
            {
                continue;
            }

            var kind = entry.State switch
            {
                EntityState.Added => EntityChangeKind.Added,
                EntityState.Deleted => EntityChangeKind.Deleted,
                _ => EntityChangeKind.Modified
            };

            var values = new Dictionary<string, object?>();
            var propertyChanges = new List<PropertyChange>();
            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (AuditColumns.Contains(name))
                {
                    continue;
                }

                var current = kind == EntityChangeKind.Deleted ? property.OriginalValue : property.CurrentValue;
                values[name] = current;

                switch (kind)
                {
                    case EntityChangeKind.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                        propertyChanges.Add(new PropertyChange(name, Text(property.OriginalValue), Text(property.CurrentValue)));
                        break;
                    case EntityChangeKind.Added when current is not null && name is not ("Id" or "TenantId"):
                        propertyChanges.Add(new PropertyChange(name, null, Text(current)));
                        break;
                    case EntityChangeKind.Deleted when current is not null && name is not ("Id" or "TenantId"):
                        propertyChanges.Add(new PropertyChange(name, Text(current), null));
                        break;
                }
            }

            // A "modified" entry whose only changes were audit stamps or navigations tells nobody anything.
            if (kind == EntityChangeKind.Modified && propertyChanges.Count == 0)
            {
                continue;
            }

            var type = entry.Metadata.ClrType;
            changes.Add(new EntityChange(
                type.Name, type.FullName ?? type.Name, entity.Id, kind, currentUserContext.UserId, now, values, propertyChanges));
        }

        return changes;
    }

    private static string? Text(object? value)
    {
        var text = value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] : text;
    }
}
