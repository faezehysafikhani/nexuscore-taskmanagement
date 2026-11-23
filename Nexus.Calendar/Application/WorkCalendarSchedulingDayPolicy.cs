using Nexus.Calendar.Application.Dtos;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Calendar.Application;

/// <summary>
/// Which days take new tasks and meetings: the organization's default work calendar when it has one (its working
/// week, its date exceptions and - when it follows them - the official holidays), so an organization's own days
/// off and extra working days count. Without a default calendar (or outside a tenant, e.g. a background job), the
/// fixed Iranian rule as before. Days before today are refused either way.
/// </summary>
/// <remarks>
/// <see cref="ISchedulingDayPolicy"/> is synchronous; the calendar is read once per request (scope) for a window
/// of days and cached, so a request that checks many dates reads it once.
/// </remarks>
public sealed class WorkCalendarSchedulingDayPolicy(
    IWorkCalendarService calendars,
    ICurrentUserContext currentUser,
    TimeProvider? clock = null) : ISchedulingDayPolicy
{
    private static readonly TimeSpan IranOffset = TimeSpan.FromMinutes(210);
    /// <summary>Days read at once (the service allows up to 1,000).</summary>
    private const int Window = 730;

    private readonly IranSchedulingDayPolicy _fallback = new(clock);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private bool _loaded;
    private Guid? _calendarId;
    private readonly Dictionary<DateOnly, CalendarDayDto> _days = new();

    public SchedulingDayDecision Check(DateOnly date)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(IranOffset).DateTime);
        if (date < today)
        {
            return new(false, "امکان ایجاد وظیفه یا جلسه برای روزهای گذشته وجود ندارد.");
        }

        var day = Day(date, today);
        if (day is null)
        {
            return _fallback.Check(date);
        }

        if (day.IsWorkingDay)
        {
            return SchedulingDayDecision.Allowed;
        }

        var why = string.IsNullOrWhiteSpace(day.Reason) ? string.Empty : $" ({day.Reason})";
        return day.Source switch
        {
            CalendarDaySource.OfficialHoliday => new(false, $"در تعطیلات رسمی امکان ایجاد وظیفه یا جلسه وجود ندارد{why}."),
            CalendarDaySource.Exception => new(false, $"این روز در تقویم کاری سازمان تعطیل است{why}."),
            _ => new(false, "این روز در تقویم کاری سازمان روز کاری نیست."),
        };
    }

    /// <summary>The day as the default calendar sees it; null when there is no default calendar to ask.</summary>
    private CalendarDayDto? Day(DateOnly date, DateOnly today)
    {
        if (!_loaded)
        {
            _loaded = true;
            if (currentUser.TenantId is { } tenantId)
            {
                // Sync over async: ISchedulingDayPolicy is synchronous, and ASP.NET Core has no synchronization
                // context to deadlock on. Read once per scope.
                var list = calendars.ListAsync(tenantId, CancellationToken.None).GetAwaiter().GetResult();
                _calendarId = list.IsSuccess ? list.Value!.FirstOrDefault(c => c.IsDefault)?.Id : null;
                if (_calendarId is { } id)
                {
                    Load(id, today, today.AddDays(Window));
                }
            }
        }

        if (_calendarId is not { } calendarId)
        {
            return null;
        }

        if (!_days.TryGetValue(date, out var day))
        {
            // Beyond the window read: that stretch too.
            Load(calendarId, date, date.AddDays(Window));
            _days.TryGetValue(date, out day);
        }

        return day;
    }

    private void Load(Guid calendarId, DateOnly from, DateOnly to)
    {
        var days = calendars.GetDaysAsync(calendarId, from, to, CancellationToken.None).GetAwaiter().GetResult();
        if (days.IsFailure)
        {
            return;
        }

        foreach (var d in days.Value!)
        {
            _days[d.Date] = d;
        }
    }
}
