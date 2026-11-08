namespace Nexus.Calendar.Application;

/// <summary>
/// An optional source of a country's official holidays. The Calendar module ships none: install one
/// (for example Nexus.Calendar.IranianHolidays) and calendars that follow official holidays use it; without one
/// they behave as if the setting were off. Dates are Gregorian; the reason is the holiday's name.
/// </summary>
public interface IOfficialHolidayProvider
{
    /// <summary>The holiday's name when the date is an official holiday, otherwise null.</summary>
    string? GetReason(DateOnly date);
}
