using Nexus.Calendar.Application;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;

namespace Nexus.Integrations.ProjectCalendar.Application;

/// <summary>Tells the Calendar module when projects still use a calendar - the Calendar module knows nothing of projects.</summary>
public sealed class ProjectCalendarUsageChecker(IProjectRepository projects) : ICalendarUsageChecker
{
    public async Task<string?> GetUsageAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken)
    {
        var page = await projects.ListAsync(new ListProjectsRequest(tenantId, PageNumber: 1, PageSize: 1, WorkCalendarId: calendarId), cancellationToken);
        return page.TotalCount == 0 ? null : $"{page.TotalCount} project(s)";
    }
}
