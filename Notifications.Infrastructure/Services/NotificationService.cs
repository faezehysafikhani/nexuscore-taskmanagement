using Microsoft.EntityFrameworkCore;
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

    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        _db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            request.TenantId ?? _currentUser.TenantId,
            request.UserId,
            request.Title,
            request.Message,
            request.Type,
            request.Category,
            request.ReferenceId,
            request.ActorUserId,
            request.ActorName));

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RetractAsync(NotificationRetraction retraction, CancellationToken cancellationToken = default)
    {
        var categories = retraction.Categories.ToList();
        var stale = await _db.Notifications
            .Where(n => n.ReferenceId == retraction.ReferenceId
                        && n.ActorUserId == retraction.ActorUserId
                        && n.TenantId == retraction.TenantId
                        && !n.IsRead
                        && n.CreatedAt >= retraction.SinceUtc
                        && n.Category != null && categories.Contains(n.Category))
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        _db.Notifications.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}