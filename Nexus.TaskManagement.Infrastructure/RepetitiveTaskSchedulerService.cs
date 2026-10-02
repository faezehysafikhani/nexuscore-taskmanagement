using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

public sealed class RepetitiveTaskSchedulerOptions
{
    public const string SectionName = "TaskManagement:Scheduler";

    /// <summary>Off by default: a host opts in rather than discovering a timer it did not ask for.</summary>
    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>Ceiling on one pass, so a large backlog cannot monopolise a run.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Next runs stored by the old calculation (the time of day taken as UTC), checked once at
    /// start-up: "Report" (default) only logs what would change, "Apply" corrects them, "Off"
    /// skips the check. See RealignLegacyOccurrencesAsync.
    /// </summary>
    public string LegacyTimeRealignment { get; set; } = "Report";
}

/// <summary>
/// Fires recurring tasks when they come due.
///
/// Follows the platform's existing precedent (Events.Infrastructure's
/// EventReminderBackgroundService): a hosted BackgroundService on a timer. NexusCore has no
/// Hangfire, Quartz or other scheduler, and introducing one for a single job would add a
/// dependency the rest of the platform does not share.
///
/// Ordering, and why it is this way round:
///   1. claim the occurrence by advancing NextExecutionAtUtc and saving
///   2. that save raises RepetitiveTaskDue, which the interceptor dispatches afterwards
///
/// Advancing first means a notification failure costs one message, never the schedule. The
/// trade is at-most-once delivery, which is the right way round for a reminder - a missed
/// reminder is an annoyance, a stuck schedule that fires forever is an outage.
///
/// Guards:
///   - a semaphore keeps two passes from overlapping inside one process
///   - the UPDATE that advances the row is conditional on the due time it was read with, so
///     two processes racing on the same row leave exactly one winner
///   - a schedule whose task has been deleted is deactivated rather than retried forever
///   - one bad row is logged and skipped; the batch continues
/// </summary>
public sealed class RepetitiveTaskSchedulerService(
    IServiceProvider serviceProvider,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    ILogger<RepetitiveTaskSchedulerService> logger) : BackgroundService
{
    private readonly RepetitiveTaskSchedulerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Recurring task scheduler is disabled. Set {Section}:Enabled to true to start it.",
                RepetitiveTaskSchedulerOptions.SectionName);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, _options.PollIntervalSeconds));
        logger.LogInformation("Recurring task scheduler started, polling every {Interval}.", interval);

        try
        {
            await RealignLegacyOccurrencesAsync(_options.LegacyTimeRealignment, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed check must not keep reminders from running.
            logger.LogError(ex, "Checking recurrence schedules for old next-run times failed.");
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessDueSchedulesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Never let one bad pass kill the loop.
                    logger.LogError(ex, "Recurring task scheduler pass failed.");
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task ProcessDueSchedulesAsync(CancellationToken cancellationToken)
    {
        // Overlap guard for this process. The conditional update below covers the
        // multi-process case.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            logger.LogDebug("Previous scheduler pass is still running; skipping this tick.");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            var calculator = scope.ServiceProvider.GetRequiredService<IRecurrenceCalculator>();

            var now = DateTimeOffset.UtcNow;

            var due = await db.RepetitiveTasks
                .Where(r => r.IsActive
                            && r.NextExecutionAtUtc != null
                            && r.NextExecutionAtUtc <= now)
                .OrderBy(r => r.NextExecutionAtUtc)
                .Take(Math.Clamp(_options.BatchSize, 1, 1000))
                .ToListAsync(cancellationToken);

            foreach (var schedule in due)
            {
                try
                {
                    await ProcessOneAsync(db, calculator, schedule, now, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another instance claimed this occurrence first. Correct outcome.
                    logger.LogDebug(
                        "Schedule {ScheduleId} was claimed by another instance.", schedule.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to process recurrence schedule {ScheduleId}.", schedule.Id);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ProcessOneAsync(
        TaskManagementDbContext db,
        IRecurrenceCalculator calculator,
        RepetitiveTask schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claimedFor = schedule.NextExecutionAtUtc;

        var taskExists = await db.Tasks
            .AnyAsync(t => t.Id == schedule.TaskId && t.TenantId == schedule.TenantId, cancellationToken);

        if (!taskExists)
        {
            // Orphaned schedule. Stop it rather than retrying it every minute forever.
            schedule.Deactivate();
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Recurrence schedule {ScheduleId} has no task and was deactivated.", schedule.Id);
            return;
        }

        var next = calculator.CalculateNextExecution(schedule, now);

        // Claim the occurrence: advance only while the row still shows the due time this pass
        // read. A racing instance that got there first leaves 0 rows affected and we stop.
        var claimed = await db.RepetitiveTasks
            .Where(r => r.Id == schedule.Id && r.NextExecutionAtUtc == claimedFor && r.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.LastExecutionAtUtc, now)
                    .SetProperty(r => r.NextExecutionAtUtc, next)
                    .SetProperty(r => r.IsActive, next != null),
                cancellationToken);

        if (claimed == 0)
        {
            logger.LogDebug("Schedule {ScheduleId} was already advanced elsewhere.", schedule.Id);
            return;
        }

        // The claim is committed. Announcing now cannot resurrect the old due time, and a
        // failure below leaves the schedule correctly advanced.
        var task = await db.Tasks
            .Include(t => t.Assignees)
            .FirstAsync(t => t.Id == schedule.TaskId, cancellationToken);

        task.RaiseRecurrenceDue(schedule.Id, claimedFor ?? now);

        // Dispatches RepetitiveTaskDue through DomainEventDispatchInterceptor.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(ReminderDeliveryEvents.OccurrenceClaimed,
            "Recurring task {TaskId} fired for {DueAt:u}; next run {NextRun:u}.",
            schedule.TaskId, claimedFor, next);
    }

    /// <summary>
    /// Before the time-zone fix, a schedule's next run was the chosen time of day taken as UTC
    /// (09:00 became 09:00 UTC, 12:30 in Tehran). Such a value is recognisable: its UTC time of
    /// day is the schedule's own time while its local time of day is not - the correct value is
    /// the other way round, and with the zone never at UTC+0 the two cannot coincide. Anything
    /// matching neither is left as it is and reported.
    ///
    /// Only NextExecutionAtUtc changes, to the same date at the chosen time in the users' zone;
    /// the schedule the user chose is untouched. The update is a direct, conditional UPDATE: no
    /// domain event, so nothing is announced by the correction itself, and a row another
    /// instance changed meanwhile is skipped. A corrected time already in the past is announced
    /// by the next regular pass, once - the old value had not fired yet.
    /// </summary>
    internal async Task<int> RealignLegacyOccurrencesAsync(string? mode, CancellationToken cancellationToken)
    {
        var apply = string.Equals(mode, "Apply", StringComparison.OrdinalIgnoreCase);
        if (!apply && !string.Equals(mode, "Report", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var calculator = scope.ServiceProvider.GetRequiredService<IRecurrenceCalculator>();
        if (!calculator.HasOffsetFromUtc)
        {
            return 0;
        }

        var schedules = await db.RepetitiveTasks
            .AsNoTracking()
            .Where(r => r.IsActive && r.NextExecutionAtUtc != null)
            .Select(r => new { r.Id, r.NextExecutionAtUtc, r.StartTime })
            .ToListAsync(cancellationToken);

        var found = 0;
        var unclear = 0;
        foreach (var schedule in schedules)
        {
            var stored = schedule.NextExecutionAtUtc!.Value;
            var timeOfDay = (schedule.StartTime ?? new TimeOnly(0, 0)).ToTimeSpan();

            if (calculator.ToLocalTime(stored).TimeOfDay == timeOfDay)
            {
                continue; // Already correct.
            }

            if (stored.UtcDateTime.TimeOfDay != timeOfDay)
            {
                unclear++;
                logger.LogWarning(ReminderDeliveryEvents.LegacyTimeFound,
                    "Recurrence schedule {ScheduleId}: next run {NextRun:u} matches neither the old nor the current calculation; left unchanged.",
                    schedule.Id, stored);
                continue;
            }

            found++;
            var corrected = calculator.FromLocal(DateOnly.FromDateTime(stored.UtcDateTime), TimeOnly.FromTimeSpan(timeOfDay));
            if (!apply)
            {
                logger.LogInformation(ReminderDeliveryEvents.LegacyTimeFound,
                    "Recurrence schedule {ScheduleId}: next run {NextRun:u} is from the old calculation; would become {Corrected:u}.",
                    schedule.Id, stored, corrected);
                continue;
            }

            var updated = await db.RepetitiveTasks
                .Where(r => r.Id == schedule.Id && r.NextExecutionAtUtc == stored)
                .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.NextExecutionAtUtc, corrected), cancellationToken);
            if (updated == 1)
            {
                logger.LogInformation(ReminderDeliveryEvents.LegacyTimeRealigned,
                    "Recurrence schedule {ScheduleId}: next run moved from {NextRun:u} to {Corrected:u}.",
                    schedule.Id, stored, corrected);
            }
        }

        if (found > 0 || unclear > 0)
        {
            logger.LogInformation(
                "Old next-run times: {Found} found ({Mode}), {Unclear} unclear and left unchanged.",
                found, apply ? "corrected" : "report only - set TaskManagement:Scheduler:LegacyTimeRealignment to Apply to correct them", unclear);
        }

        return found;
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}
