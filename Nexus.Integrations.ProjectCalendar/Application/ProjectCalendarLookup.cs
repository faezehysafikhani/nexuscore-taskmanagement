using Nexus.Calendar.Application;
using Nexus.ProjectManagement.Core.Application;

namespace Nexus.Integrations.ProjectCalendar.Application;

/// <summary>A project's calendar must exist in the project's own tenant.</summary>
public sealed class ProjectCalendarLookup(IWorkCalendarRepository repository) : IProjectCalendarLookup
{
    public async Task<bool> ExistsAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(calendarId, cancellationToken) is { } calendar && calendar.TenantId == tenantId;
}
