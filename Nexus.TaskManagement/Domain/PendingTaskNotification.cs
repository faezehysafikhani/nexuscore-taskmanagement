namespace Nexus.TaskManagement.Domain;

/// <summary>
/// One in-app notification waiting to be delivered to one person (the notification outbox). A row is written before
/// the notification is attempted, so a failed attempt is retried later and a row that was already delivered is never
/// sent again: <see cref="DedupeKey"/> is unique, and a row is claimed with a conditional update before it is sent.
/// </summary>
public sealed class PendingTaskNotification
{
    /// <summary>After this many attempts a row is left alone (and kept for an administrator to read).</summary>
    public const int MaxAttempts = 8;

    private PendingTaskNotification()
    {
        DedupeKey = string.Empty;
        Title = string.Empty;
    }

    public PendingTaskNotification(
        Guid id, Guid tenantId, Guid taskId, string title, string? description, DateOnly dueDate, string? dueAtText,
        Guid recipientUserId, string dedupeKey, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        TaskId = taskId;
        Title = title;
        Description = description;
        DueDate = dueDate;
        DueAtText = dueAtText;
        RecipientUserId = recipientUserId;
        DedupeKey = dedupeKey;
        CreatedAtUtc = now;
        NextAttemptAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? DueAtText { get; private set; }
    public Guid RecipientUserId { get; private set; }

    /// <summary>What makes this notification the same one: task, occurrence and person.</summary>
    public string DedupeKey { get; private set; }

    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
