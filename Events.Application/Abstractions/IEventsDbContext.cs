using Events.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Events.Application.Abstractions;

public interface IEventsDbContext
{
    DbSet<CalendarEvent> Events { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
