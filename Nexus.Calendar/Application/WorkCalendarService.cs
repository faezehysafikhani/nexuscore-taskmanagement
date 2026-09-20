using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Application;

public sealed class WorkCalendarService(
    IWorkCalendarRepository repository,
    ICalendarUnitOfWork unitOfWork) : IWorkCalendarService
{
    public async Task<Result<IReadOnlyList<WorkCalendarDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var calendars = await repository.ListAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<WorkCalendarDto>>(calendars.Select(ToDto).ToList());
    }

    public async Task<Result<WorkCalendarDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        return calendar is null
            ? Result.Failure<WorkCalendarDto>(Error.NotFound("Work calendar not found."))
            : Result.Success(ToDto(calendar));
    }

    public async Task<Result<WorkCalendarDto>> CreateAsync(CreateWorkCalendarRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<WorkCalendarDto>(Error.Validation("Name is required."));
        }

        var calendar = new WorkCalendar(Guid.NewGuid(), request.TenantId, request.Name, request.WorkingDays, request.IsDefault);
        await repository.AddAsync(calendar, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calendar));
    }

    public async Task<Result<WorkCalendarDto>> UpdateAsync(Guid id, UpdateWorkCalendarRequest request, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        if (calendar is null)
        {
            return Result.Failure<WorkCalendarDto>(Error.NotFound("Work calendar not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<WorkCalendarDto>(Error.Validation("Name is required."));
        }

        calendar.Update(request.Name, request.Description, request.WorkingDays, request.IsDefault);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calendar));
    }

    public async Task<Result<WorkCalendarDto>> AddExceptionAsync(Guid id, AddWorkCalendarExceptionRequest request, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        if (calendar is null)
        {
            return Result.Failure<WorkCalendarDto>(Error.NotFound("Work calendar not found."));
        }

        calendar.AddException(Guid.NewGuid(), request.Date, request.IsWorkingDay, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calendar));
    }

    public async Task<Result<WorkCalendarDto>> RemoveExceptionAsync(Guid id, Guid exceptionId, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        if (calendar is null)
        {
            return Result.Failure<WorkCalendarDto>(Error.NotFound("Work calendar not found."));
        }

        calendar.RemoveException(exceptionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calendar));
    }

    public async Task<Result<bool>> IsWorkingDayAsync(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        return calendar is null
            ? Result.Failure<bool>(Error.NotFound("Work calendar not found."))
            : Result.Success(calendar.IsWorkingDay(date));
    }

    private static WorkCalendarDto ToDto(WorkCalendar calendar) => new(
        calendar.Id,
        calendar.TenantId,
        calendar.Name,
        calendar.Description,
        calendar.WorkingDays,
        calendar.IsDefault,
        calendar.Exceptions.Select(e => new WorkCalendarExceptionDto(e.Id, e.Date, e.IsWorkingDay, e.Description)).ToList());
}
