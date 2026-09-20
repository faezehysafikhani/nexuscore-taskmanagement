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
    IReadOnlyList<WorkCalendarExceptionDto> Exceptions);

public sealed record CreateWorkCalendarRequest(Guid TenantId, string Name, DayOfWeekMask WorkingDays, bool IsDefault);

public sealed record UpdateWorkCalendarRequest(string Name, string? Description, DayOfWeekMask WorkingDays, bool IsDefault);

public sealed record AddWorkCalendarExceptionRequest(DateOnly Date, bool IsWorkingDay, string? Description);
