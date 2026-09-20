using NexusCore.SharedKernel.Domain;

namespace Nexus.Calendar.Domain;

/// <summary>
/// Reusable platform capability: a named set of working days plus date-specific exceptions
/// (holidays or extra working days). Consumed optionally by Project (as a scheduling
/// reference) and Action. Distinct from Events.CalendarEvent (an appointment/reminder) -
/// this models which days are working days, not scheduled events.
/// </summary>
public sealed class WorkCalendar : AuditableEntity<Guid>
{
    private readonly List<WorkCalendarException> _exceptions = [];

    private WorkCalendar() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public WorkCalendar(Guid id, Guid tenantId, string name, DayOfWeekMask workingDays, bool isDefault = false) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        WorkingDays = workingDays;
        IsDefault = isDefault;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public DayOfWeekMask WorkingDays { get; private set; }
    public bool IsDefault { get; private set; }
    public IReadOnlyCollection<WorkCalendarException> Exceptions => _exceptions.AsReadOnly();

    public void Update(string name, string? description, DayOfWeekMask workingDays, bool isDefault)
    {
        Name = name.Trim();
        Description = description;
        WorkingDays = workingDays;
        IsDefault = isDefault;
    }

    public void AddException(Guid exceptionId, DateOnly date, bool isWorkingDay, string? description)
    {
        if (_exceptions.Any(exception => exception.Date == date))
        {
            return;
        }

        _exceptions.Add(new WorkCalendarException(exceptionId, Id, date, isWorkingDay, description));
    }

    public void RemoveException(Guid exceptionId) =>
        _exceptions.RemoveAll(exception => exception.Id == exceptionId);

    /// <summary>Business-rule default: the weekly pattern unless a specific date overrides it.</summary>
    public bool IsWorkingDay(DateOnly date)
    {
        var exception = _exceptions.SingleOrDefault(e => e.Date == date);
        if (exception is not null)
        {
            return exception.IsWorkingDay;
        }

        return WorkingDays.HasFlag(ToMask(date.DayOfWeek));
    }

    private static DayOfWeekMask ToMask(DayOfWeek dayOfWeek) => (DayOfWeekMask)(1 << (int)dayOfWeek);
}

[Flags]
public enum DayOfWeekMask
{
    None = 0,
    Sunday = 1 << DayOfWeek.Sunday,
    Monday = 1 << DayOfWeek.Monday,
    Tuesday = 1 << DayOfWeek.Tuesday,
    Wednesday = 1 << DayOfWeek.Wednesday,
    Thursday = 1 << DayOfWeek.Thursday,
    Friday = 1 << DayOfWeek.Friday,
    Saturday = 1 << DayOfWeek.Saturday,
    /// <summary>Sat-Wed working week (Iran default): everything except Thursday/Friday.</summary>
    IranWorkWeek = Saturday | Sunday | Monday | Tuesday | Wednesday,
    AllDays = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}
