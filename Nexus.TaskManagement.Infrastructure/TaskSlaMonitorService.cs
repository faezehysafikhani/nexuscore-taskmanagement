using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Watches the SLA clocks (phase 7): a task whose SLA due moment has passed and that is not done or paused is marked
/// breached once, its owner and the people responsible are told, and the "SLA breached" rules run. Follows the same
/// opt-in as the other jobs (TaskManagement:Scheduler:Enabled).
/// </summary>
public sealed class TaskSlaMonitorService(
    IServiceProvider serviceProvider,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    TimeProvider clock,
    ILogger<TaskSlaMonitorService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "SLA monitor pass failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass; public so a test (or an operator) can run it.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            var automation = scope.ServiceProvider.GetRequiredService<ITaskAutomation>();
            var notices = scope.ServiceProvider.GetRequiredService<ITaskNoticePublisher>();

            var due = await db.Tasks.IgnoreQueryFilters().Include(t => t.Assignees)
                .Where(t => t.ArchivedAtUtc == null && t.SlaDueAtUtc != null && t.SlaDueAtUtc <= now && t.SlaBreachedAtUtc == null
                    && t.SlaPausedAtUtc == null && t.Status != TaskItemStatus.Completed)
                .OrderBy(t => t.SlaDueAtUtc).Take(100).ToListAsync(cancellationToken);

            foreach (var task in due)
            {
                task.MarkSlaBreached(now);
                await db.SaveChangesAsync(cancellationToken);
                var people = TaskPeople.Of(task).ToList();
                if (people.Count > 0)
                {
                    await notices.PublishAsync(
                        new TaskNotice(task.TenantId, task.Id, task.Title, null, TaskNoticeKind.Activity, "SLA breached", $"Due {task.SlaDueAtUtc:u}", people), cancellationToken);
                }

                await automation.RunAsync(AutomationTrigger.SlaBreached, task, cancellationToken);
            }

            return due.Count;
        }
        finally
        {
            _gate.Release();
        }
    }
}
