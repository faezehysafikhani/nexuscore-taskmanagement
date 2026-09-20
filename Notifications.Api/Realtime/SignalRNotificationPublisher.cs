using Microsoft.AspNetCore.SignalR;
using Notifications.Api.Hubs;
using Notifications.Application.Abstractions;
using Notifications.Application.Common.Dtos;

namespace Notifications.Api.Realtime;

/// <summary>
/// SignalR transport for <see cref="INotificationRealtimePublisher"/>.
/// Targets the per-user group that <see cref="NotificationHub"/> joins on connect.
/// </summary>
public sealed class SignalRNotificationPublisher(IHubContext<NotificationHub> hub) : INotificationRealtimePublisher
{
    public Task PublishAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group($"user_{userId}")
            .SendAsync("NotificationReceived", notification, cancellationToken);
}
