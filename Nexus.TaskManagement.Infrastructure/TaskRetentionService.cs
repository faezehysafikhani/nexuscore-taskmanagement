using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>Applies each organization's retention (see RetentionService), once a day; opt-in with the module's other jobs.</summary>
public sealed class TaskRetentionBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    TimeProvider clock,
    ILogger<TaskRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await PurgeAsync(clock.GetUtcNow(), stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Retention removed {Count} old log row(s).", removed);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Retention pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over every organization that set a retention; returns how many rows were removed.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var removed = 0;
        foreach (var setting in await db.TaskRetentionSettings.ToListAsync(cancellationToken))
        {
            if (setting.AutomationLogDays is { } logDays)
            {
                var before = now.AddDays(-logDays);
                var old = await db.AutomationLog.Where(l => l.TenantId == setting.TenantId && l.AtUtc < before).Take(5000).ToListAsync(cancellationToken);
                db.AutomationLog.RemoveRange(old);
                removed += old.Count;
            }

            if (setting.WebhookDeliveryDays is { } hookDays)
            {
                // Only what is finished (delivered or given up): a row still being retried is never removed.
                var before = now.AddDays(-hookDays);
                var old = await db.WebhookDeliveries
                    .Where(d => d.TenantId == setting.TenantId && d.CreatedAtUtc < before && (d.DeliveredAtUtc != null || d.GaveUpAtUtc != null))
                    .Take(5000).ToListAsync(cancellationToken);
                db.WebhookDeliveries.RemoveRange(old);
                removed += old.Count;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return removed;
    }
}
