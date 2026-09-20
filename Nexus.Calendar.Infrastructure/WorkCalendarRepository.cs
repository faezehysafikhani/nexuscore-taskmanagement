using Microsoft.EntityFrameworkCore;
using Nexus.Calendar.Application;
using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Infrastructure;

public sealed class WorkCalendarRepository(CalendarDbContext dbContext) : IWorkCalendarRepository
{
    public Task<WorkCalendar?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WorkCalendars.Include(calendar => calendar.Exceptions)
            .SingleOrDefaultAsync(calendar => calendar.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkCalendar>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.WorkCalendars
            .Include(calendar => calendar.Exceptions)
            .Where(calendar => calendar.TenantId == tenantId)
            .OrderBy(calendar => calendar.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WorkCalendar calendar, CancellationToken cancellationToken)
    {
        await dbContext.WorkCalendars.AddAsync(calendar, cancellationToken);
    }
}
