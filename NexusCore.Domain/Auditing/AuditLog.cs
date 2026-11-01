using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Auditing;

public sealed class AuditLog : Entity<Guid>
{
    private AuditLog() : base(Guid.Empty)
    {
        Action = string.Empty;
    }

    public AuditLog(Guid id, Guid? tenantId, Guid? userId, string action, string? entityName, string? entityId, string? details, string? ipAddress, Guid? sessionId = null, string? userAgent = null) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Action = action;
        EntityName = entityName;
        EntityId = entityId;
        Details = details;
        IpAddress = ipAddress;
        SessionId = sessionId;
        UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; }
    public string? EntityName { get; private set; }
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }
    public string? IpAddress { get; private set; }
    /// <summary>The session (sid) the entry belongs to, when there is one.</summary>
    public Guid? SessionId { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}
