using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Works out the next firing time for a schedule.
///
/// Day numbering follows the UI, which is Jalali: 0 = Saturday … 6 = Friday. That is not
/// <see cref="DayOfWeek"/>'s numbering (0 = Sunday), so every conversion goes through
/// <see cref="ToJalaliDayIndex"/> rather than casting.
///
/// Dates and the StartTime (with none set, midnight) are the users' wall-clock values, in the
/// configured time zone (<see cref="RecurrenceOptions"/>); the result is a UTC moment. The
/// parameterless constructor keeps everything in UTC.
/// </summary>
public sealed class RecurrenceCalculator(TimeZoneInfo timeZone) : IRecurrenceCalculator
{
    public RecurrenceCalculator() : this(TimeZoneInfo.Utc)
    {
    }

    /// <summary>The configured zone, or UTC (with the reason logged by the caller) when the id is unknown.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    public DateTimeOffset ToLocalTime(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, timeZone);

    public DateTimeOffset FromLocal(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public bool HasOffsetFromUtc => timeZone.BaseUtcOffset != TimeSpan.Zero || timeZone.SupportsDaylightSavingTime;

    public DateTimeOffset? CalculateNextExecution(RepetitiveTask schedule, DateTimeOffset afterUtc)
    {
        if (!schedule.IsActive)
        {
            return null;
        }

        var timeOfDay = schedule.StartTime ?? new TimeOnly(0, 0);

        // Never look further ahead than two years - a schedule whose rules can never match
        // (an empty weekly day list, say) must terminate instead of spinning.
        var cursor = DateOnly.FromDateTime(ToLocalTime(afterUtc).Date);
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
                var candidate = FromLocal(cursor, timeOfDay);
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
