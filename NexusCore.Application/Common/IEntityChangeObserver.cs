namespace NexusCore.Application.Common;

public enum EntityChangeKind
{
    Added = 0,
    Modified = 1,
    Deleted = 2
}

/// <summary>One property that changed: its name and the value before and after, as text (null = no value).</summary>
public sealed record PropertyChange(string Property, string? OldValue, string? NewValue);

/// <summary>
/// One entity that was written by a successful SaveChanges: what it is, what happened to it, who did it,
/// every one of its current (for a delete, last) values, and which of those changed. Values are the
/// entity's own columns only (no navigations). Audit stamps (CreatedAtUtc, ModifiedByUserId, ...) are left out.
/// </summary>
public sealed record EntityChange(
    string EntityName,
    string EntityTypeFullName,
    Guid? EntityId,
    EntityChangeKind Kind,
    Guid? UserId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<PropertyChange> Changes);

/// <summary>
/// An optional listener for what every module's DbContext writes. Register implementations in DI and
/// AuditingInterceptor (already on every module's context) calls them once a save has succeeded; with
/// none registered it does nothing extra. An observer runs after the save is committed, so it can never
/// fail or roll back the business operation - an exception it throws is logged and swallowed - and it
/// must not write through a context that has AuditingInterceptor itself, or it would observe its own writes.
/// </summary>
public interface IEntityChangeObserver
{
    Task OnChangesSavedAsync(IReadOnlyList<EntityChange> changes, CancellationToken cancellationToken);
}
