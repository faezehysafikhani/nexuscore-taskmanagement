using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// The notification outbox on this module's database. Every step uses a scope of its own: a failure while
/// delivering one row never leaves something half-saved in a context that is then reused.
/// </summary>
public sealed class TaskNotificationOutbox(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<TaskNotificationOutbox> logger) : ITaskNotificationOutbox
{
    /// <summary>How long a row is held by the server that took it; if that server dies, another takes it after this.</summary>
    internal static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>Delivered rows are kept this long, then removed.</summary>
    internal static readonly TimeSpan KeepDelivered = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<Guid>> EnqueueAsync(
        TaskDueNotification notification, string occurrenceKey, CancellationToken cancellationToken)
    {
        if (notification.RecipientUserIds.Count == 0)
        {
            return [];
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var now = time.GetUtcNow();

        var rows = notification.RecipientUserIds.Distinct().Select(userId => new PendingTaskNotification(
            Guid.NewGuid(), notification.TenantId, notification.TaskId, Trim(notification.Title, 200), Trim(notification.Description, 4000),
            notification.DueDate, Trim(notification.DueAtText, 100), userId,
            $"{notification.TaskId:N}:{occurrenceKey}:{userId:N}", now)).ToList();

        var keys = rows.Select(r => r.DedupeKey).ToList();
        var existing = await db.NotificationOutbox.Where(r => keys.Contains(r.DedupeKey))
            .Select(r => r.DedupeKey).ToListAsync(cancellationToken);
        var fresh = rows.Where(r => !existing.Contains(r.DedupeKey)).ToList();
        if (fresh.Count == 0)
        {
            return [];
        }

        db.NotificationOutbox.AddRange(fresh);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Another server queued the same occurrence between the check and the save: it will deliver it.
            logger.LogInformation(ex, "Notifications of task {TaskId} were queued by someone else; not queued again.", notification.TaskId);
            return [];
        }

        return fresh.Select(r => r.Id).ToList();
    }

    public async Task<int> DeliverAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        List<PendingTaskNotification> due;
        var now = time.GetUtcNow();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            var query = db.NotificationOutbox.AsNoTracking()
                .Where(r => r.DeliveredAtUtc == null && r.NextAttemptAtUtc <= now && r.Attempts < PendingTaskNotification.MaxAttempts);
            if (ids is not null)
            {
                var wanted = ids.ToList();
                query = query.Where(r => wanted.Contains(r.Id));
            }

            due = await query.OrderBy(r => r.NextAttemptAtUtc).Take(200).ToListAsync(cancellationToken);

            if (ids is null)
            {
                var old = now - KeepDelivered;
                await db.NotificationOutbox.Where(r => r.DeliveredAtUtc != null && r.DeliveredAtUtc < old).ExecuteDeleteAsync(cancellationToken);
            }
        }

        var delivered = 0;
        foreach (var row in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await DeliverOneAsync(row, cancellationToken))
            {
                delivered++;
            }
        }

        return delivered;
    }

    private async Task<bool> DeliverOneAsync(PendingTaskNotification row, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var now = time.GetUtcNow();

        // Taken before it is sent: only the server whose update changes the row sends it, so it is never sent twice.
        var claimed = await db.NotificationOutbox
            .Where(r => r.Id == row.Id && r.DeliveredAtUtc == null && r.NextAttemptAtUtc <= now && r.Attempts < PendingTaskNotification.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.NextAttemptAtUtc, now + Lease), cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        var attempt = row.Attempts + 1;
        string? error = null;
        try
        {
            var publisher = scope.ServiceProvider.GetRequiredService<ITaskNotificationPublisher>();
            var notification = new TaskDueNotification(
                row.TenantId, row.TaskId, row.Title, row.Description, row.DueDate, [row.RecipientUserId], row.DueAtText);
            if (publisher is IReliableTaskNotificationPublisher reliable)
            {
                if (!await reliable.TryPublishAsync(notification, cancellationToken))
                {
                    error = "The notification could not be stored.";
                }
            }
            else
            {
                await publisher.PublishAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            error = Trim(ex.Message, 500);
        }

        if (error is null)
        {
            await db.NotificationOutbox.Where(r => r.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeliveredAtUtc, now).SetProperty(r => r.LastError, (string?)null), cancellationToken);
            return true;
        }

        var next = now + Backoff(attempt);
        logger.LogWarning(
            "Notification {Id} for task {TaskId} was not delivered (attempt {Attempt}); next try {Next}. {Error}",
            row.Id, row.TaskId, attempt, next, error);
        await db.NotificationOutbox.Where(r => r.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.NextAttemptAtUtc, next).SetProperty(r => r.LastError, error), cancellationToken);
        return false;
    }

    /// <summary>30 s, 1 min, 2 min ... at most 1 hour.</summary>
    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempt - 1))));

    private static string? Trim(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}

/// <summary>Delivers what the outbox holds: first attempts that failed, and rows left by a server that stopped.</summary>
public sealed class TaskNotificationOutboxService(
    IServiceScopeFactory scopes,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    ILogger<TaskNotificationOutboxService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var delivered = await scope.ServiceProvider.GetRequiredService<ITaskNotificationOutbox>().DeliverAsync(null, stoppingToken);
                if (delivered > 0)
                {
                    logger.LogInformation("Delivered {Count} notification(s) from the outbox.", delivered);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "The notification outbox pass failed.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
