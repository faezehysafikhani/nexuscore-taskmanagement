using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using Notifications.Application.Abstractions;
using Notifications.Domain.Entities;

namespace Notifications.Infrastructure.Persistence;

public class NotificationDbContext : DbContext, INotificationDbContext
{
    private readonly ITenantScope? _tenantScope;

    public NotificationDbContext(
        DbContextOptions<NotificationDbContext> options,
        ITenantScope? tenantScope = null)
        : base(options)
    {
        _tenantScope = tenantScope;
    }

    // The organization of the signed-in user, or null when nothing confines the work (ITenantScope).
    private Guid? TenantFilterId => _tenantScope?.FilterTenantIdFor(nameof(NotificationDbContext));

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Other organizations' notifications are not returned, whatever the query says.
        modelBuilder.Entity<Notification>().HasQueryFilter(notification => TenantFilterId == null || notification.TenantId == TenantFilterId);
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.Property(n => n.Category).HasMaxLength(64);
            notification.Property(n => n.ActorName).HasMaxLength(200);
        });
    }
}