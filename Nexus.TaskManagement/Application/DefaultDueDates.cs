using NexusCore.Application.Common;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// A due date the server fills in itself (quick add, message to task, a request made a task): today, or the next
/// day the organization works when today is a day off - so a default never lands on a date the same server refuses.
/// </summary>
internal static class DefaultDueDates
{
    private static readonly TimeSpan IranOffset = TimeSpan.FromHours(3.5);

    public static DateOnly TodayInIran() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(IranOffset).DateTime);

    /// <summary>The first working day from <paramref name="start"/> (itself when it is one); at most 60 days ahead.</summary>
    public static DateOnly FirstWorkingDay(DateOnly start, ISchedulingDayPolicy? days)
    {
        if (days is null)
        {
            return start;
        }

        for (var i = 0; i < 60; i++)
        {
            var day = start.AddDays(i);
            if (days.Check(day).IsAllowed)
            {
                return day;
            }
        }

        return start;
    }
}
