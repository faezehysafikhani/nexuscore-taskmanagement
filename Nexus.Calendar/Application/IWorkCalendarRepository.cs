using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Application;

public interface IWorkCalendarRepository
{
    Task<WorkCalendar?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkCalendar>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddAsync(WorkCalendar calendar, CancellationToken cancellationToken);
}
