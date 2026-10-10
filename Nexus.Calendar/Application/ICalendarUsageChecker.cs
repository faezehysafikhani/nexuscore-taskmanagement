namespace Nexus.Calendar.Application;

/// <summary>
/// Lets other modules say that a calendar is in use, without the Calendar module knowing them: every installed
/// module that points at a calendar (actions, projects, ...) registers one, and a calendar that any of them
/// reports cannot be deleted. Returns a short description of the use ("2 actions"), or null when it is not used.
/// </summary>
public interface ICalendarUsageChecker
{
    Task<string?> GetUsageAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken);
}
