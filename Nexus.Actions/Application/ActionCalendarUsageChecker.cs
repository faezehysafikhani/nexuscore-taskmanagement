using Nexus.Calendar.Application;

namespace Nexus.Actions.Application;

/// <summary>Tells the Calendar module when actions still use a calendar.</summary>
public sealed class ActionCalendarUsageChecker(IActionItemRepository repository) : ICalendarUsageChecker
{
    public async Task<string?> GetUsageAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken)
    {
        var count = (await repository.ListAsync(tenantId, projectId: null, cancellationToken)).Count(a => a.WorkCalendarId == calendarId);
        return count == 0 ? null : $"{count} action(s)";
    }
}
