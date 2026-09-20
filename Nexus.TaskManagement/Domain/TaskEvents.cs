using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// Raised when a task is created. Dispatched by DomainEventDispatchInterceptor after
/// SaveChanges, so another installed module can react without TaskManagement referencing it.
/// </summary>
public sealed record TaskItemCreated(Guid TaskId, Guid TenantId, string Title, bool IsProject) : DomainEvent;

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
