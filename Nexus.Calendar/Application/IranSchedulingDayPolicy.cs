using System.Globalization;
using NexusCore.Application.Common;

namespace Nexus.Calendar.Application;

/// <summary>Days before today (Iran time), Friday and the official Iranian holidays supported by the product calendar take no new tasks or meetings.</summary>
public sealed class IranSchedulingDayPolicy(TimeProvider? clock = null) : ISchedulingDayPolicy
{
    private static readonly PersianCalendar Persian = new();
    private static readonly TimeSpan IranOffset = TimeSpan.FromMinutes(210);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly HashSet<(int Month, int Day)> Fixed =
    [
        (1,1),(1,2),(1,3),(1,4),(1,12),(1,13),(3,14),(3,15),(11,22),(12,29)
    ];

    private static readonly HashSet<(int Year, int Month, int Day)> Variable =
    [
        (1403,1,12),(1403,1,22),(1403,1,23),(1403,2,15),(1403,3,29),(1403,4,5),(1403,4,25),(1403,4,26),(1403,6,4),(1403,6,12),(1403,6,14),(1403,6,22),(1403,6,31),(1403,9,15),(1403,10,24),(1403,11,8),(1403,11,26),
        (1404,1,1),(1404,1,11),(1404,1,12),(1404,2,4),(1404,3,17),(1404,3,23),(1404,4,14),(1404,4,15),(1404,5,24),(1404,6,1),(1404,6,3),(1404,6,11),(1404,6,20),(1404,9,4),(1404,10,13),(1404,10,27),(1404,11,15),
        (1405,1,1),(1405,1,2),(1405,1,25),(1405,3,6),(1405,3,15),(1405,4,3),(1405,4,4),(1405,5,13),(1405,5,21),(1405,5,22),(1405,5,30),(1405,6,8),(1405,8,22),(1405,10,2),(1405,10,16),(1405,11,4),(1405,12,9),(1405,12,19),(1405,12,20),
        (1406,1,14),(1406,2,27),(1406,3,4),(1406,3,25),(1406,3,26),(1406,5,3),(1406,5,11),(1406,5,12),(1406,5,20),(1406,5,29),(1406,8,12),(1406,9,21),(1406,10,5),(1406,10,23),(1406,11,29),(1406,12,8),(1406,12,9)
    ];

    public SchedulingDayDecision Check(DateOnly date)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(IranOffset).DateTime);
        if (date < today)
            return new(false, "امکان ایجاد وظیفه یا جلسه برای روزهای گذشته وجود ندارد.");
        if (date.DayOfWeek == DayOfWeek.Friday)
            return new(false, "در روز جمعه امکان ایجاد وظیفه یا جلسه وجود ندارد.");
        var value = date.ToDateTime(TimeOnly.MinValue);
        var key = (Persian.GetYear(value), Persian.GetMonth(value), Persian.GetDayOfMonth(value));
        return Fixed.Contains((key.Item2, key.Item3)) || Variable.Contains(key)
            ? new(false, "در تعطیلات رسمی ایران امکان ایجاد وظیفه یا جلسه وجود ندارد.")
            : SchedulingDayDecision.Allowed;
    }
}
