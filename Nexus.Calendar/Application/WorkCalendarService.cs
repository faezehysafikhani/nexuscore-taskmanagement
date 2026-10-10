using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Application;

public sealed class WorkCalendarService(
    IWorkCalendarRepository repository,
    ICalendarUnitOfWork unitOfWork,
    IOfficialHolidayProvider? officialHolidays = null,
    IEnumerable<ICalendarUsageChecker>? usageCheckers = null) : IWorkCalendarService
{
    public const int MaxDaysPerQuery = 1000;

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

        var hoursError = ValidateHours(request.WorkHoursPerDay);
        if (hoursError is not null)
        {
            return Result.Failure<WorkCalendarDto>(hoursError);
        }

        var calendar = new WorkCalendar(Guid.NewGuid(), request.TenantId, request.Name, request.WorkingDays, request.IsDefault);
        if (request.Description is not null)
        {
            calendar.Update(request.Name, request.Description, request.WorkingDays, request.IsDefault);
        }

        calendar.SetPolicy(request.WorkHoursPerDay ?? 8, request.ApplyOfficialHolidays ?? true);
        if (request.Exceptions is not null)
        {
            calendar.ReplaceExceptions(request.Exceptions.Select(e => (e.Date, e.IsWorkingDay, e.Description)));
        }

        if (request.IsDefault)
        {
            await ClearOtherDefaultsAsync(request.TenantId, calendar.Id, cancellationToken);
        }

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

        var hoursError = ValidateHours(request.WorkHoursPerDay);
        if (hoursError is not null)
        {
            return Result.Failure<WorkCalendarDto>(hoursError);
        }

        calendar.Update(request.Name, request.Description, request.WorkingDays, request.IsDefault);
        calendar.SetPolicy(request.WorkHoursPerDay ?? calendar.WorkHoursPerDay, request.ApplyOfficialHolidays ?? calendar.ApplyOfficialHolidays);
        if (request.Exceptions is not null)
        {
            calendar.ReplaceExceptions(request.Exceptions.Select(e => (e.Date, e.IsWorkingDay, e.Description)));
        }

        if (request.IsDefault)
        {
            await ClearOtherDefaultsAsync(calendar.TenantId, calendar.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calendar));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        if (calendar is null)
        {
            return Result.Failure(Error.NotFound("Work calendar not found."));
        }

        if (calendar.IsDefault)
        {
            return Result.Failure(Error.Conflict("The default calendar cannot be deleted; make another calendar the default first."));
        }

        // Whatever uses the calendar (actions, projects...) says so through its own checker; the calendar knows none of them.
        foreach (var checker in usageCheckers ?? [])
        {
            if (await checker.GetUsageAsync(calendar.TenantId, id, cancellationToken) is { } usage)
            {
                return Result.Failure(Error.Conflict($"The calendar is in use ({usage}) and cannot be deleted."));
            }
        }

        await repository.RemoveAsync(calendar, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
            : Result.Success(CalendarDayResolver.Resolve(calendar, date, officialHolidays).IsWorkingDay);
    }

    public async Task<Result<IReadOnlyList<CalendarDayDto>>> GetDaysAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rangeError = ValidateRange(from, to);
        if (rangeError is not null)
        {
            return Result.Failure<IReadOnlyList<CalendarDayDto>>(rangeError);
        }

        var calendar = await repository.GetByIdAsync(id, cancellationToken);
        if (calendar is null)
        {
            return Result.Failure<IReadOnlyList<CalendarDayDto>>(Error.NotFound("Work calendar not found."));
        }

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => CalendarDayResolver.Resolve(calendar, from.AddDays(offset), officialHolidays))
            .ToList();
        return Result.Success<IReadOnlyList<CalendarDayDto>>(days);
    }

    public Task<Result<IReadOnlyList<OfficialHolidayDto>>> ListOfficialHolidaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rangeError = ValidateRange(from, to);
        if (rangeError is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<OfficialHolidayDto>>(rangeError));
        }

        // No provider installed: there simply are no official holidays to list.
        IReadOnlyList<OfficialHolidayDto> holidays = officialHolidays is null
            ? []
            : Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(offset => from.AddDays(offset))
                .Select(date => (Date: date, Reason: officialHolidays.GetReason(date)))
                .Where(x => x.Reason is not null)
                .Select(x => new OfficialHolidayDto(x.Date, x.Reason!))
                .ToList();
        return Task.FromResult(Result.Success(holidays));
    }

    /// <summary>There is one default calendar per tenant: making a calendar the default makes every other one not.</summary>
    private async Task ClearOtherDefaultsAsync(Guid tenantId, Guid keepId, CancellationToken cancellationToken)
    {
        foreach (var other in (await repository.ListAsync(tenantId, cancellationToken)).Where(c => c.IsDefault && c.Id != keepId))
        {
            other.Update(other.Name, other.Description, other.WorkingDays, isDefault: false);
        }
    }

    private static Error? ValidateHours(int? hours) =>
        hours is < 1 or > 24 ? Error.Validation("Work hours per day must be between 1 and 24.") : null;

    private static Error? ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return Error.Validation("'to' cannot be before 'from'.");
        }

        return to.DayNumber - from.DayNumber + 1 > MaxDaysPerQuery
            ? Error.Validation($"At most {MaxDaysPerQuery} days can be asked for at once.")
            : null;
    }

    private static WorkCalendarDto ToDto(WorkCalendar calendar) => new(
        calendar.Id,
        calendar.TenantId,
        calendar.Name,
        calendar.Description,
        calendar.WorkingDays,
        calendar.IsDefault,
        calendar.Exceptions.Select(e => new WorkCalendarExceptionDto(e.Id, e.Date, e.IsWorkingDay, e.Description)).ToList(),
        calendar.WorkHoursPerDay,
        calendar.ApplyOfficialHolidays);
}
