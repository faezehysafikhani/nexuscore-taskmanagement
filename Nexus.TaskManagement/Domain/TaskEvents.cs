using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// Raised when a task is created. Dispatched by DomainEventDispatchInterceptor after
/// SaveChanges, so another installed module can react without TaskManagement referencing it.
/// </summary>
public sealed record TaskItemCreated(Guid TaskId, Guid TenantId, string Title, bool IsProject) : DomainEvent;

/// <summary>
/// Raised for each person who became responsible for an existing task (AssignedUserId is that
/// person). Not raised on creation (<see cref="TaskItemCreated"/> covers that) nor for someone
/// who already was responsible.
/// </summary>
public sealed record TaskItemAssigneeChanged(
    Guid TaskId,
    Guid TenantId,
    Guid AssignedUserId,
    Guid? PreviousAssignedUserId,
    Guid? ChangedByUserId) : DomainEvent;

/// <summary>
/// Raised when the due date or time of an existing task really changed. ResponsibleUserIds are
/// those who were responsible before and still are (a new responsible person is told the due
/// date anyway); null means every responsible person.
/// </summary>
public sealed record TaskItemDueChanged(
    Guid TaskId,
    Guid TenantId,
    Guid? ChangedByUserId,
    IReadOnlyList<Guid>? ResponsibleUserIds = null) : DomainEvent;

/// <summary>Raised when a task changes status.</summary>
public sealed record TaskItemStatusChanged(Guid TaskId, Guid TenantId, TaskItemStatus Status) : DomainEvent;

/// <summary>
/// Raised when a recurring task falls due. This is the seam the future background job will use
/// to reach Notifications and SMS: TaskManagement publishes this and never references either
/// module. Nothing handles it yet.
/// </summary>
public sealed record RepetitiveTaskDue(
    Guid RepetitiveTaskId,
    Guid TaskId,
    Guid TenantId,
    DateTimeOffset DueAtUtc) : DomainEvent;
