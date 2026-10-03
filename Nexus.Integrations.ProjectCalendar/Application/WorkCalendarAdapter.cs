using Nexus.Calendar.Domain;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;

namespace Nexus.Integrations.ProjectCalendar.Application;

/// <summary>
/// A <see cref="WorkCalendar"/> seen as Waterfall's <see cref="IWorkingDayCalendar"/>. Same rule
/// as <see cref="WorkCalendar.IsWorkingDay"/> - a date-specific exception overrides the weekly
/// pattern - but with the exceptions indexed once, because a schedule asks about thousands of dates.
/// </summary>
public sealed class WorkCalendarAdapter : IWorkingDayCalendar
{
    private readonly DayOfWeekMask _workingDays;
    private readonly Dictionary<DateOnly, bool> _exceptions;

    public WorkCalendarAdapter(WorkCalendar calendar)
    {
        _workingDays = calendar.WorkingDays;
        _exceptions = calendar.Exceptions
            .GroupBy(exception => exception.Date)
            .ToDictionary(group => group.Key, group => group.First().IsWorkingDay);
    }

    public bool IsWorkingDay(DateOnly date) =>
        _exceptions.TryGetValue(date, out var isWorkingDay)
            ? isWorkingDay
            : _workingDays.HasFlag((DayOfWeekMask)(1 << (int)date.DayOfWeek));
}
