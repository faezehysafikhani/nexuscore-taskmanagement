using Nexus.Calendar.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Application;

public interface IWorkCalendarService
{
    Task<Result<IReadOnlyList<WorkCalendarDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> CreateAsync(CreateWorkCalendarRequest request, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> UpdateAsync(Guid id, UpdateWorkCalendarRequest request, CancellationToken cancellationToken);
    /// <summary>Refused (409) while anything - an action, a project - still uses the calendar.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> AddExceptionAsync(Guid id, AddWorkCalendarExceptionRequest request, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> RemoveExceptionAsync(Guid id, Guid exceptionId, CancellationToken cancellationToken);
    Task<Result<bool>> IsWorkingDayAsync(Guid id, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Every day of the range as the calendar sees it (at most 1,000 days).</summary>
    Task<Result<IReadOnlyList<CalendarDayDto>>> GetDaysAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The official holidays in the range from the installed provider; empty when none is installed.</summary>
    Task<Result<IReadOnlyList<OfficialHolidayDto>>> ListOfficialHolidaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
