using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application;
using Nexus.TaskManagement.Application;

namespace Nexus.Integrations.CalendarTasks;

/// <summary>
/// SLA working days from the organization's default work calendar (CAT-85): its working week, its date exceptions
/// and, when it follows them, the official holidays. No default calendar - or a host without the calendar module's
/// work calendars - : null, and the host's rule decides.
/// </summary>
public sealed class WorkCalendarSlaDays(IServiceProvider services) : ISlaWorkingDays
{
    public async Task<Func<DateOnly, bool>?> GetAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var calendars = services.GetService<IWorkCalendarService>();
        if (calendars is null)
        {
            return null;
        }

        var list = await calendars.ListAsync(tenantId, cancellationToken);
        var calendar = list.IsSuccess ? list.Value!.FirstOrDefault(c => c.IsDefault) : null;
        if (calendar is null)
        {
            return null;
        }

        var days = await calendars.GetDaysAsync(calendar.Id, from, to, cancellationToken);
        if (days.IsFailure)
        {
            return null;
        }

        var working = days.Value!.ToDictionary(d => d.Date, d => d.IsWorkingDay);
        return day => !working.TryGetValue(day, out var isWorking) || isWorking;
    }
}
