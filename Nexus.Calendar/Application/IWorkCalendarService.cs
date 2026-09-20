using Nexus.Calendar.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Application;

public interface IWorkCalendarService
{
    Task<Result<IReadOnlyList<WorkCalendarDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> CreateAsync(CreateWorkCalendarRequest request, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> UpdateAsync(Guid id, UpdateWorkCalendarRequest request, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> AddExceptionAsync(Guid id, AddWorkCalendarExceptionRequest request, CancellationToken cancellationToken);
    Task<Result<WorkCalendarDto>> RemoveExceptionAsync(Guid id, Guid exceptionId, CancellationToken cancellationToken);
    Task<Result<bool>> IsWorkingDayAsync(Guid id, DateOnly date, CancellationToken cancellationToken);
}
