using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Reminds people of their tasks before it is too late, once each, in the app and by SMS:
///   - due soon: a day before a due time (at the morning hour the day before a due date only);
///   - overdue: when the due moment passed and the task is not done (or paused) - once;
///   - coming up: before each occurrence of a recurring task (an hour, by default);
/// and sends the task SMS that waited for the quiet hours to end.
///
/// Runs with the recurring-task scheduler (TaskManagement:Scheduler, same switch and interval).
/// Each reminder is claimed with a conditional update before it is sent, so two servers never
/// both send it and a pass that runs again sends nothing twice; a moved due date or the next
/// occurrence is reminded of anew. Delivery is at-most-once, like the occurrence reminders.
/// </summary>
public sealed class TaskReminderService(
    IServiceProvider serviceProvider,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    TimeProvider clock,
    ILogger<TaskReminderService> logger) : BackgroundService
{
    /// <summary>A reminder later than this after its moment (e.g. the server was off) is skipped, not sent late.</summary>
    private static readonly TimeSpan MaxLateness = TimeSpan.FromHours(36);

    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, options.Value.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Task reminder pass failed.");
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

    /// <summary>One pass at <paramref name="now"/>.</summary>
    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = new Pass(
                services.GetRequiredService<TaskManagementDbContext>(),
                services.GetRequiredService<IRecurrenceCalculator>(),
                services.GetRequiredService<ITaskReminderSettingsService>(),
                services.GetRequiredService<TaskReminderDispatcher>(),
                now);

            await RemindOfDueTasksAsync(context, cancellationToken);
            await RemindOfComingOccurrencesAsync(context, cancellationToken);
            await SendHeldBackSmsAsync(context.Db, services.GetRequiredService<ITaskSmsSender>(), now, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record Pass(
        TaskManagementDbContext Db, IRecurrenceCalculator Calendar, ITaskReminderSettingsService Settings,
        TaskReminderDispatcher Dispatcher, DateTimeOffset Now)
    {
        private readonly Dictionary<Guid, TaskReminderSettingsDto> _settings = [];

        public async Task<TaskReminderSettingsDto> SettingsOfAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            if (!_settings.TryGetValue(tenantId, out var settings))
            {
                _settings[tenantId] = settings = await Settings.ReadAsync(tenantId, cancellationToken);
            }

            return settings;
        }
    }

    // ---------------------------------------------------------------- due soon / overdue

    private async Task RemindOfDueTasksAsync(Pass pass, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(pass.Calendar.ToLocalTime(pass.Now).DateTime);
        var (from, to) = (today.AddDays(-2), today.AddDays(8));

        // Open one-off tasks due around now; a recurring task is reminded of per occurrence instead.
        var tasks = await pass.Db.Tasks
            .IgnoreQueryFilters()
            .Include(t => t.Assignees)
            .Where(t => t.Recurrence == null
                        && t.Status != TaskItemStatus.Completed && t.Status != TaskItemStatus.Paused
                        && t.DueDate >= from && t.DueDate <= to)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        foreach (var task in tasks)
        {
            try
            {
                var settings = await pass.SettingsOfAsync(task.TenantId, cancellationToken);
                var dueAt = task.DueTime is { } time
                    ? pass.Calendar.FromLocal(task.DueDate, time)
                    : pass.Calendar.FromLocal(task.DueDate.AddDays(1), TimeOnly.MinValue);
                var morning = new TimeOnly(settings.MorningHour, 0);
                var dueSoonAt = task.DueTime is not null
                    ? dueAt.AddHours(-settings.DueSoonHoursBefore)
                    : pass.Calendar.FromLocal(task.DueDate.AddDays(-1), morning);
                var overdueAt = task.DueTime is not null ? dueAt : pass.Calendar.FromLocal(task.DueDate.AddDays(1), morning);
                var when = TaskDates.Format(task.DueDate, task.DueTime);
                var responsible = ResponsibleOrOwner(task);

                // Not for a task created after its reminder time: whoever got it knows how near it is.
                if (settings.DueSoonEnabled
                    && pass.Now >= dueSoonAt && pass.Now < dueAt && pass.Now - dueSoonAt < MaxLateness
                    && task.DueSoonNotifiedFor != dueAt && task.CreatedAtUtc < dueSoonAt
                    && await pass.Db.Tasks.IgnoreQueryFilters()
                        .Where(t => t.Id == task.Id && (t.DueSoonNotifiedFor == null || t.DueSoonNotifiedFor != dueAt))
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.DueSoonNotifiedFor, (DateTimeOffset?)dueAt), cancellationToken) == 1)
                {
                    await pass.Dispatcher.SendAsync(task, TaskNoticeKind.DueSoon, when, responsible, task.ResponsibleUserIds, cancellationToken);
                }

                if (settings.OverdueEnabled
                    && pass.Now >= overdueAt && pass.Now - overdueAt < MaxLateness
                    && task.OverdueNotifiedFor != dueAt && task.CreatedAtUtc < dueAt
                    && await pass.Db.Tasks.IgnoreQueryFilters()
                        .Where(t => t.Id == task.Id && (t.OverdueNotifiedFor == null || t.OverdueNotifiedFor != dueAt))
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.OverdueNotifiedFor, (DateTimeOffset?)dueAt), cancellationToken) == 1)
                {
                    // Those responsible, and its creator - who is waiting for it.
                    var app = responsible.Concat(task.OwnerUserId is { } owner ? [owner] : []).Distinct().ToList();
                    await pass.Dispatcher.SendAsync(task, TaskNoticeKind.Overdue, when, app, task.ResponsibleUserIds, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Due reminders of task {TaskId} failed.", task.Id);
            }
        }
    }

    // ---------------------------------------------------------------- recurring: coming up

    private async Task RemindOfComingOccurrencesAsync(Pass pass, CancellationToken cancellationToken)
    {
        var horizon = pass.Now.AddMinutes(1440);
        var schedules = await pass.Db.RepetitiveTasks
            .IgnoreQueryFilters()
            .Where(r => r.IsActive && r.NextExecutionAtUtc != null && r.NextExecutionAtUtc > pass.Now && r.NextExecutionAtUtc <= horizon)
            .ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            try
            {
                var settings = await pass.SettingsOfAsync(schedule.TenantId, cancellationToken);
                var next = schedule.NextExecutionAtUtc!.Value;
                var remindAt = next.AddMinutes(-settings.RecurringSoonMinutesBefore);
                if (!settings.RecurringSoonEnabled || pass.Now < remindAt || schedule.UpcomingNotifiedFor == next
                    || schedule.CreatedAtUtc > remindAt)
                {
                    continue;
                }

                // Claimed before it is sent: never twice, even by two servers.
                var claimed = await pass.Db.RepetitiveTasks.IgnoreQueryFilters()
                    .Where(r => r.Id == schedule.Id && (r.UpcomingNotifiedFor == null || r.UpcomingNotifiedFor != next))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpcomingNotifiedFor, (DateTimeOffset?)next), cancellationToken);
                if (claimed != 1)
                {
                    continue;
                }

                var task = await pass.Db.Tasks.IgnoreQueryFilters().Include(t => t.Assignees)
                    .FirstOrDefaultAsync(t => t.Id == schedule.TaskId, cancellationToken);
                if (task is null)
                {
                    continue;
                }

                // Like the occurrence itself: those responsible and those on the access list in the
                // app (its creator when it has nobody else), SMS for those responsible.
                var local = pass.Calendar.ToLocalTime(next);
                var when = TaskDates.Format(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
                var app = task.ResponsibleUserIds.Concat(task.Assignees.Select(a => a.UserId)).Distinct().ToList();
                if (app.Count == 0 && task.OwnerUserId is { } owner)
                {
                    app.Add(owner);
                }

                await pass.Dispatcher.SendAsync(task, TaskNoticeKind.RecurringSoon, when, app, task.ResponsibleUserIds, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Coming-up reminder of schedule {ScheduleId} failed.", schedule.Id);
            }
        }
    }

    // ---------------------------------------------------------------- quiet hours

    private async Task SendHeldBackSmsAsync(TaskManagementDbContext db, ITaskSmsSender sms, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.PendingSms
            .Where(p => p.SendAfterUtc <= now)
            .OrderBy(p => p.SendAfterUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var pending in due)
        {
            try
            {
                // Taken out before it is sent: never sent twice, even by two servers.
                if (await db.PendingSms.Where(p => p.Id == pending.Id).ExecuteDeleteAsync(cancellationToken) == 0)
                {
                    continue;
                }

                var sent = await sms.SendAsync(pending.TenantId, pending.PhoneNumber, pending.Text, cancellationToken);
                if (sent.IsFailure)
                {
                    logger.LogWarning("A task SMS held for the quiet hours was not sent: {Error}", sent.Error.Message);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "A task SMS held for the quiet hours failed.");
            }
        }
    }

    private static IReadOnlyList<Guid> ResponsibleOrOwner(TaskItem task) =>
        task.ResponsibleUserIds.Count > 0 ? task.ResponsibleUserIds : task.OwnerUserId is { } owner ? [owner] : [];
}
