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
        string type)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Title = title;
        Message = message;
        Type = type;
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

    public void MarkAsRead()
    {
        IsRead = true;
    }
}