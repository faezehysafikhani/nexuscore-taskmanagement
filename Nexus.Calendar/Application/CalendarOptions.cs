namespace Nexus.Calendar.Application;

/// <summary>Opt-in behaviour of the Calendar module. Every switch defaults to off, so a project that
/// already uses the module sees exactly what it always saw.</summary>
public sealed class CalendarOptions
{
    /// <summary>When on, making a calendar the default clears the flag on the tenant's other calendars,
    /// so there is only ever one. Off (the default) leaves the flags as they were set.</summary>
    public bool SingleDefaultPerTenant { get; set; }
}
