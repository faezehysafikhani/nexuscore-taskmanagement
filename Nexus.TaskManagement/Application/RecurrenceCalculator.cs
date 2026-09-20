using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Works out the next firing time for a schedule.
///
/// Day numbering follows the UI, which is Jalali: 0 = Saturday … 6 = Friday. That is not
/// <see cref="DayOfWeek"/>'s numbering (0 = Sunday), so every conversion goes through
/// <see cref="ToJalaliDayIndex"/> rather than casting.
///
/// Everything is computed in UTC. Times of day come from the schedule's StartTime; with none
/// set, midnight is used.
/// </summary>
public sealed class RecurrenceCalculator : IRecurrenceCalculator
{
    public DateTimeOffset? CalculateNextExecution(RepetitiveTask schedule, DateTimeOffset afterUtc)
    {
        if (!schedule.IsActive)
        {
            return null;
        }

        var timeOfDay = schedule.StartTime ?? new TimeOnly(0, 0);

        // Never look further ahead than two years - a schedule whose rules can never match
        // (an empty weekly day list, say) must terminate instead of spinning.
        var cursor = DateOnly.FromDateTime(afterUtc.UtcDateTime.Date);
        if (cursor < schedule.StartDate)
        {
            cursor = schedule.StartDate;
        }

        var limit = cursor.AddYears(2);

        while (cursor <= limit)
        {
            if (schedule.EndDate is { } endDate && cursor > endDate)
            {
                return null;
            }

            if (Matches(schedule, cursor))
            {
                var candidate = new DateTimeOffset(cursor.ToDateTime(timeOfDay), TimeSpan.Zero);
                if (candidate > afterUtc)
                {
                    return candidate;
                }
            }

            cursor = cursor.AddDays(1);
        }

        return null;
    }

    private static bool Matches(RepetitiveTask schedule, DateOnly date) => schedule.Frequency switch
    {
        RecurrenceFrequency.Daily => true,

        RecurrenceFrequency.Weekly =>
            schedule.WeeklyDays.Contains(ToJalaliDayIndex(date.DayOfWeek))
            && IsOnInterval(schedule, date),

        RecurrenceFrequency.Monthly or RecurrenceFrequency.MonthlyDay =>
            schedule.MonthlyDays.Contains(date.Day),

        RecurrenceFrequency.MonthlyNthWeekday =>
            schedule.NthWeekday is { } weekday
            && schedule.NthOccurrence is { } nth
            && ToJalaliDayIndex(date.DayOfWeek) == weekday
            && IsNthOccurrenceInMonth(date, nth),

        _ => false
    };

    /// <summary>
    /// Weekly schedules may repeat every N weeks. Whole weeks elapsed since the start date
    /// decide whether this week is one of them.
    /// </summary>
    private static bool IsOnInterval(RepetitiveTask schedule, DateOnly date)
    {
        var interval = schedule.IntervalWeeks ?? 1;
        if (interval <= 1)
        {
            return true;
        }

        var elapsedDays = date.DayNumber - schedule.StartDate.DayNumber;
        if (elapsedDays < 0)
        {
            return false;
        }

        return (elapsedDays / 7) % interval == 0;
    }

    private static bool IsNthOccurrenceInMonth(DateOnly date, OccurrenceNth nth)
    {
        if (nth == OccurrenceNth.Last)
        {
            // Last one of its weekday in the month: another 7 days would spill into the next.
            return date.AddDays(7).Month != date.Month;
        }

        var ordinal = (date.Day - 1) / 7;
        return ordinal == (int)nth;
    }

    /// <summary>Converts .NET's Sunday-first numbering to the UI's Saturday-first numbering.</summary>
    internal static int ToJalaliDayIndex(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 1) % 7;
}
