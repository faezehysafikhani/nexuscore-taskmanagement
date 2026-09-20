using NexusCore.SharedKernel.Domain;

namespace Nexus.Calendar.Domain;

public sealed class WorkCalendarException : Entity<Guid>
{
    private WorkCalendarException() : base(Guid.Empty)
    {
    }

    internal WorkCalendarException(Guid id, Guid calendarId, DateOnly date, bool isWorkingDay, string? description) : base(id)
    {
        CalendarId = calendarId;
        Date = date;
        IsWorkingDay = isWorkingDay;
        Description = description;
    }

    public Guid CalendarId { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>False = holiday (override a working day off). True = extra working day (override a weekend on).</summary>
    public bool IsWorkingDay { get; private set; }
    public string? Description { get; private set; }
}
