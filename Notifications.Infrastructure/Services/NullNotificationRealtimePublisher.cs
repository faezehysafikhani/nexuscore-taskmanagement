using Notifications.Application.Abstractions;
using Notifications.Application.Common.Dtos;

namespace Notifications.Infrastructure.Services;

/// <summary>
/// Fallback used when no transport is registered (e.g. a background host with no SignalR).
/// Notifications are still persisted; only the live push is skipped.
/// </summary>
public sealed class NullNotificationRealtimePublisher : INotificationRealtimePublisher
{
    public Task PublishAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
