using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Application.Dtos;

public sealed record WorkCalendarExceptionDto(Guid Id, DateOnly Date, bool IsWorkingDay, string? Description);

public sealed record WorkCalendarDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    DayOfWeekMask WorkingDays,
    bool IsDefault,
    IReadOnlyList<WorkCalendarExceptionDto> Exceptions,
    int WorkHoursPerDay = 8,
    bool ApplyOfficialHolidays = false);

/// <summary>WorkHoursPerDay omitted = 8; ApplyOfficialHolidays omitted = on (a new calendar follows the official holidays when a provider is installed).</summary>
public sealed record CreateWorkCalendarRequest(
    Guid TenantId, string Name, DayOfWeekMask WorkingDays, bool IsDefault,
    int? WorkHoursPerDay = null, bool? ApplyOfficialHolidays = null, string? Description = null,
    IReadOnlyList<AddWorkCalendarExceptionRequest>? Exceptions = null);

/// <summary>Omitted (null) WorkHoursPerDay / ApplyOfficialHolidays leave the stored value alone. Exceptions, when given, REPLACE the
/// calendar's date-specific exceptions (so a screen can save the whole calendar in one call); omitted leaves them alone.</summary>
public sealed record UpdateWorkCalendarRequest(
    string Name, string? Description, DayOfWeekMask WorkingDays, bool IsDefault,
    int? WorkHoursPerDay = null, bool? ApplyOfficialHolidays = null,
    IReadOnlyList<AddWorkCalendarExceptionRequest>? Exceptions = null);

public sealed record AddWorkCalendarExceptionRequest(DateOnly Date, bool IsWorkingDay, string? Description);

public enum CalendarDaySource
{
    /// <summary>The weekly working-day pattern decided it.</summary>
    Weekly = 0,

    /// <summary>A date-specific exception of this calendar decided it.</summary>
    Exception = 1,

    /// <summary>An official holiday (applied because the calendar follows them) made it a non-working day.</summary>
    OfficialHoliday = 2
}

/// <summary>One day as the calendar sees it: working or not, and why (Reason is the exception's text or the holiday's name).</summary>
public sealed record CalendarDayDto(DateOnly Date, bool IsWorkingDay, CalendarDaySource Source, string? Reason);

public sealed record OfficialHolidayDto(DateOnly Date, string Reason);
