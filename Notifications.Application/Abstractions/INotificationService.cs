namespace Notifications.Application.Abstractions;

public interface INotificationService
{
    Task NotifyAsync(
        Guid userId,
        string title,
        string message,
        string type,
        CancellationToken cancellationToken = default,
        Guid? tenantId = null);

    /// <summary>
    /// A notification with what it is about (category, the thing, who did it), so the client can
    /// show and open it. Without an implementation of its own it is stored as a plain one.
    /// </summary>
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default) =>
        NotifyAsync(request.UserId, request.Title, request.Message, request.Type, cancellationToken, request.TenantId);

    /// <summary>
    /// Takes back unread notifications another one now says better (e.g. a short "task updated"
    /// once the full description of the same edit arrives). Returns how many were removed.
    /// </summary>
    Task<int> RetractAsync(NotificationRetraction retraction, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

public sealed record NotificationRequest(
    Guid UserId,
    string Title,
    string Message,
    string Type,
    Guid? TenantId = null,
    string? Category = null,
    Guid? ReferenceId = null,
    Guid? ActorUserId = null,
    string? ActorName = null);

/// <summary>Unread notifications of these categories, about one thing, caused by one person since a moment.</summary>
public sealed record NotificationRetraction(
    Guid? TenantId,
    IReadOnlyCollection<string> Categories,
    Guid ReferenceId,
    Guid ActorUserId,
    DateTime SinceUtc);
