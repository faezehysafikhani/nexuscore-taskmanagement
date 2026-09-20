using Microsoft.EntityFrameworkCore;
using Nexus.Calendar.Application;
using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Infrastructure;

public sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options)
    : DbContext(options), ICalendarUnitOfWork
{
    public DbSet<WorkCalendar> WorkCalendars => Set<WorkCalendar>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CalendarDbContext).Assembly);
    }
}
