using NexusCore.SharedKernel.Domain;

namespace Notifications.Domain.Entities;

public class Notification : AuditableEntity<Guid>
{
    private Notification() : base(Guid.Empty) { }

    public Notification(
        Guid id,
        Guid? tenantId,
        Guid userId,
        string title,
        string message,
        string type,
        string? category = null,
        Guid? referenceId = null,
        Guid? actorUserId = null,
        string? actorName = null)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Title = title;
        Message = message;
        Type = type;
        Category = category;
        ReferenceId = referenceId;
        ActorUserId = actorUserId;
        ActorName = actorName;
        IsRead = false;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>What it is about, for the client to show it (e.g. "task.comment"); null for a plain notice.</summary>
    public string? Category { get; private set; }

    /// <summary>The thing it is about (e.g. the task's id), so the client can open it.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>Who caused it, when a person did.</summary>
    public Guid? ActorUserId { get; private set; }

    public string? ActorName { get; private set; }

    public void MarkAsRead()
    {
        IsRead = true;
    }
}