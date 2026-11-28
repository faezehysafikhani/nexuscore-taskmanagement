namespace Nexus.ProjectManagement.Core.Application;

/// <summary>
/// Optional: whether a work calendar exists in a tenant, so a project cannot point at a calendar that is gone or belongs to
/// another organization (M2). Installed by Nexus.Integrations.ProjectCalendar; without it the id is stored unchecked, as before.
/// </summary>
public interface IProjectCalendarLookup
{
    Task<bool> ExistsAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken);
}
