namespace Notifications.Application.Abstractions;

public interface INotificationService
{
    Task NotifyAsync(
        Guid userId,
        string title,
        string message,
        string type,
        CancellationToken cancellationToken = default);
}