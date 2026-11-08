using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Application;

/// <summary>
/// The one rule for whether a date is a working day: a date-specific exception of the calendar wins (it can
/// open a Friday or close a Monday, official holiday or not); otherwise the weekly pattern; and a day the weekly
/// pattern calls working is still off when the calendar follows official holidays and one falls on it.
/// </summary>
public static class CalendarDayResolver
{
    public static CalendarDayDto Resolve(WorkCalendar calendar, DateOnly date, IOfficialHolidayProvider? holidays)
    {
        var exception = calendar.Exceptions.FirstOrDefault(e => e.Date == date);
        if (exception is not null)
        {
            return new CalendarDayDto(date, exception.IsWorkingDay, CalendarDaySource.Exception, exception.Description);
        }

        if (!calendar.WorkingDays.HasFlag((DayOfWeekMask)(1 << (int)date.DayOfWeek)))
        {
            return new CalendarDayDto(date, false, CalendarDaySource.Weekly, null);
        }

        if (calendar.ApplyOfficialHolidays && holidays?.GetReason(date) is { } reason)
        {
            return new CalendarDayDto(date, false, CalendarDaySource.OfficialHoliday, reason);
        }

        return new CalendarDayDto(date, true, CalendarDaySource.Weekly, null);
    }
}
