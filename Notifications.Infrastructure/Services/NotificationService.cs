using NexusCore.SharedKernel.Interfaces;
using Notifications.Application.Abstractions;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public NotificationService(
        INotificationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task NotifyAsync(
        Guid userId,
        string title,
        string message,
        string type,
        CancellationToken cancellationToken = default,
        Guid? tenantId = null)
    {
        // Background work (scheduled reminders) has no signed-in user: it names the tenant.
        var notification = new Notification(
            Guid.NewGuid(),
            tenantId ?? _currentUser.TenantId,
            userId,
            title,
            message,
            type);

        _db.Notifications.Add(notification);

        await _db.SaveChangesAsync(cancellationToken);
    }
}