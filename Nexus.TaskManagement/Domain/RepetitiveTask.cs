using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// The recurrence schedule for one task - and nothing else. Title, description, priority,
/// assignee and user group all stay on the <see cref="TaskItem"/> this row points at; they are
/// deliberately not duplicated here.
///
/// One task owns at most one schedule (enforced by a unique index on TaskId), so a plain task
/// simply has no row.
///
/// Shaped for the background job that will eventually drive it: it scans rows where
/// IsActive is set and NextExecutionAtUtc has come due, follows TaskId to read the task and
/// its people, raises the notification, then advances the schedule.
/// </summary>
public sealed class RepetitiveTask : AuditableEntity<Guid>
{
    private RepetitiveTask() : base(Guid.Empty)
    {
        WeeklyDays = [];
        MonthlyDays = [];
    }

    public RepetitiveTask(
        Guid id,
        Guid tenantId,
        Guid taskId,
        RecurrenceFrequency frequency,
        DateOnly startDate) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        Frequency = frequency;
        StartDate = startDate;
        IsActive = true;
        WeeklyDays = [];
        MonthlyDays = [];
    }

    public Guid TenantId { get; private set; }

    /// <summary>Real foreign key to the task this schedule belongs to. Unique - one schedule per task.</summary>
    public Guid TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Weekly only: 1 = every week, 2 = every second week, ...</summary>
    public int? IntervalWeeks { get; private set; }

    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }

    /// <summary>Weekly only. 0 = Saturday ... 6 = Friday, matching the UI's Jalali week.</summary>
    public List<int> WeeklyDays { get; private set; }

    /// <summary>Monthly / MonthlyDay only. Days 1-31.</summary>
    public List<int> MonthlyDays { get; private set; }

    /// <summary>MonthlyNthWeekday only.</summary>
    public OccurrenceNth? NthOccurrence { get; private set; }

    /// <summary>MonthlyNthWeekday only. 0 = Saturday ... 6 = Friday.</summary>
    public int? NthWeekday { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    // --- Scheduling state, owned by the future background job ---
    public DateTimeOffset? NextExecutionAtUtc { get; private set; }
    public DateTimeOffset? LastExecutionAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    public void UpdateSchedule(
        RecurrenceFrequency frequency,
        int? intervalWeeks,
        TimeOnly? startTime,
        TimeOnly? endTime,
        IEnumerable<int>? weeklyDays,
        IEnumerable<int>? monthlyDays,
        OccurrenceNth? nthOccurrence,
        int? nthWeekday,
        DateOnly startDate,
        DateOnly? endDate)
    {
        Frequency = frequency;
        IntervalWeeks = frequency == RecurrenceFrequency.Weekly ? intervalWeeks : null;
        StartTime = startTime;
        EndTime = endTime;
        WeeklyDays = frequency == RecurrenceFrequency.Weekly
            ? (weeklyDays ?? []).Distinct().OrderBy(d => d).ToList()
            : [];
        MonthlyDays = frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.MonthlyDay
            ? (monthlyDays ?? []).Distinct().OrderBy(d => d).ToList()
            : [];
        NthOccurrence = frequency == RecurrenceFrequency.MonthlyNthWeekday ? nthOccurrence : null;
        NthWeekday = frequency == RecurrenceFrequency.MonthlyNthWeekday ? nthWeekday : null;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Called by the background job after it has dispatched an occurrence.</summary>
    public void MarkExecuted(DateTimeOffset executedAtUtc, DateTimeOffset? nextExecutionAtUtc)
    {
        LastExecutionAtUtc = executedAtUtc;
        NextExecutionAtUtc = nextExecutionAtUtc;
        if (nextExecutionAtUtc is null)
        {
            IsActive = false;
        }
    }

    public void SetNextExecution(DateTimeOffset? nextExecutionAtUtc) => NextExecutionAtUtc = nextExecutionAtUtc;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
