namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>Which dates are working days. Waterfall depends on nothing that owns calendars: the
/// optional ProjectCalendar integration supplies a real one from Nexus.Calendar, and without it
/// every date counts as a working day.</summary>
public interface IWorkingDayCalendar
{
    bool IsWorkingDay(DateOnly date);
}

/// <summary>Resolves the work calendar a project points at. Registered with a default that knows
/// no calendars (so everything is a working day); an integration replaces it.</summary>
public interface IWorkingDayCalendarProvider
{
    /// <summary>The calendar, or null when there is none (no id, unknown id, a calendar of
    /// another tenant, or no integration installed).</summary>
    Task<IWorkingDayCalendar?> GetAsync(Guid tenantId, Guid? calendarId, CancellationToken cancellationToken);
}

/// <summary>Every date is a working day: the plan is measured in plain calendar days.</summary>
public sealed class AllDaysCalendar : IWorkingDayCalendar
{
    public static readonly AllDaysCalendar Instance = new();

    public bool IsWorkingDay(DateOnly date) => true;
}

public sealed class NullWorkingDayCalendarProvider : IWorkingDayCalendarProvider
{
    public Task<IWorkingDayCalendar?> GetAsync(Guid tenantId, Guid? calendarId, CancellationToken cancellationToken) =>
        Task.FromResult<IWorkingDayCalendar?>(null);
}
