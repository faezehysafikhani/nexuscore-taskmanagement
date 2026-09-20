using Notifications.Application.Common.Dtos;

namespace Notifications.Application.Abstractions;

/// <summary>
/// Pushes a freshly created notification to the recipient's open clients.
/// Declared here (not in Infrastructure) so the persistence layer never has to reference SignalR.
/// </summary>
public interface INotificationRealtimePublisher
{
    Task PublishAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default);
}
