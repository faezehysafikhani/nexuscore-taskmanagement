using Nexus.Calendar.Application;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;

namespace Nexus.Integrations.ProjectCalendar.Application;

public sealed class WorkCalendarProvider(IWorkCalendarRepository repository, IOfficialHolidayProvider? officialHolidays = null) : IWorkingDayCalendarProvider
{
    public async Task<IWorkingDayCalendar?> GetAsync(Guid tenantId, Guid? calendarId, CancellationToken cancellationToken)
    {
        if (calendarId is null)
        {
            return null;
        }

        var calendar = await repository.GetByIdAsync(calendarId.Value, cancellationToken);

        // A calendar of another tenant is treated as missing rather than leaked into this plan.
        return calendar is null || calendar.TenantId != tenantId ? null : new WorkCalendarAdapter(calendar, officialHolidays);
    }
}
