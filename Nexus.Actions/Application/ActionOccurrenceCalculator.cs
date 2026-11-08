using System.Globalization;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Application;

/// <summary>
/// The occurrences of a repeating action inside its own date range, from its rule. A one-off action (no rule, or no complete range)
/// has a single occurrence. Months and the day of the month follow the Jalali calendar.
/// </summary>
public static class ActionOccurrenceCalculator
{
    public const int DefaultMaxCount = 100;

    /// <summary>The weekdays a weekly rule falls on when none are chosen: Saturday to Wednesday.</summary>
    private static readonly int[] DefaultWeekdays = [6, 0, 1, 2, 3];

    private static readonly PersianCalendar Jalali = new();

    public static IReadOnlyList<ActionOccurrenceDto> Calculate(ActionItem action, int maxCount = DefaultMaxCount) => Calculate(
        action.RecurrenceUnit, action.RecurrenceInterval ?? 1, action.StartDate, action.EndDate,
        action.RecurrenceDaysOfWeek, action.RecurrenceDayOfMonth, action.RecurrenceMonths, action.RecurrenceDurationHours, maxCount);

    public static IReadOnlyList<ActionOccurrenceDto> Calculate(
        ActionRecurrenceUnit? unit, int interval, DateOnly? start, DateOnly? end,
        int? daysOfWeekMask, int? dayOfMonth, int? monthsMask, decimal? durationHours, int maxCount = DefaultMaxCount)
    {
        if (unit is not { } rule || start is not { } from || end is not { } to || to < from)
        {
            var only = end ?? start ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return [new ActionOccurrenceDto(1, start is { } s && end is null ? s : only, durationHours)];
        }

        interval = Math.Max(1, interval);
        var dates = new List<DateOnly>();

        switch (rule)
        {
            case ActionRecurrenceUnit.Daily:
                for (var day = from; day <= to && dates.Count < maxCount; day = day.AddDays(interval))
                {
                    dates.Add(day);
                }

                break;

            case ActionRecurrenceUnit.Weekly:
                var weekdays = daysOfWeekMask is { } wm && wm != 0
                    ? Enumerable.Range(0, 7).Where(d => (wm & (1 << d)) != 0).ToHashSet()
                    : DefaultWeekdays.ToHashSet();
                for (var day = from; day <= to && dates.Count < maxCount; day = day.AddDays(1))
                {
                    var week = (day.DayNumber - from.DayNumber) / 7;
                    if (week % interval == 0 && weekdays.Contains((int)day.DayOfWeek))
                    {
                        dates.Add(day);
                    }
                }

                break;

            case ActionRecurrenceUnit.Monthly:
                var targetDay = dayOfMonth ?? 1;
                var startMonth = MonthIndex(from);
                for (var day = from; day <= to && dates.Count < maxCount; day = day.AddDays(1))
                {
                    var (_, month, jd) = Split(day);
                    var inMonth = monthsMask is not { } mm || mm == 0 || (mm & (1 << (month - 1))) != 0;
                    var inStep = (MonthIndex(day) - startMonth) % interval == 0;
                    // Days 31 (and 30 in Esfand) that a month lacks fall on its last day instead.
                    var onDay = jd == targetDay || (targetDay > DaysInMonth(day) && jd == DaysInMonth(day));
                    if (inMonth && inStep && onDay)
                    {
                        dates.Add(day);
                    }
                }

                break;

            case ActionRecurrenceUnit.Yearly:
                for (var index = 0; dates.Count < maxCount; index++)
                {
                    var day = from.AddYears(index * interval);
                    if (day > to)
                    {
                        break;
                    }

                    dates.Add(day);
                }

                break;
        }

        if (dates.Count == 0)
        {
            dates.Add(from);
        }

        return dates.Select((date, i) => new ActionOccurrenceDto(i + 1, date, durationHours)).ToList();
    }

    private static (int Year, int Month, int Day) Split(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return (Jalali.GetYear(value), Jalali.GetMonth(value), Jalali.GetDayOfMonth(value));
    }

    private static int MonthIndex(DateOnly date)
    {
        var (year, month, _) = Split(date);
        return year * 12 + month;
    }

    private static int DaysInMonth(DateOnly date)
    {
        var (year, month, _) = Split(date);
        return Jalali.GetDaysInMonth(year, month);
    }
}
