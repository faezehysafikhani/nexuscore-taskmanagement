using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Entities;

namespace Notifications.Application.Abstractions;

public interface INotificationDbContext
{
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}