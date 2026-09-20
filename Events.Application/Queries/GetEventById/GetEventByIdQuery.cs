using Events.Application.Abstractions;
using Events.Application.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Events.Application.Queries.GetEventById;

public record GetEventByIdQuery(Guid Id) : IRequest<Result<EventDto>>;

public class GetEventByIdQueryHandler : IRequestHandler<GetEventByIdQuery, Result<EventDto>>
{
    private readonly IEventsDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetEventByIdQueryHandler(IEventsDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<EventDto>> Handle(GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || !_currentUser.TenantId.HasValue)
        {
            return Result.Failure<EventDto>(Error.Unauthorized());
        }

        var calendarEvent = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.TenantId == _currentUser.TenantId.Value && e.UserId == _currentUser.UserId.Value, cancellationToken);

        if (calendarEvent is null)
        {
            return Result.Failure<EventDto>(Error.NotFound("Event not found."));
        }

        var dto = new EventDto(
            calendarEvent.Id,
            calendarEvent.TenantId,
            calendarEvent.UserId,
            calendarEvent.Title,
            calendarEvent.Description,
            calendarEvent.StartAtUtc,
            calendarEvent.EndAtUtc,
            calendarEvent.IsCompleted,
            calendarEvent.ReminderMinutesBefore,
            calendarEvent.ReminderSent,
            calendarEvent.CreatedAtUtc,
            calendarEvent.ModifiedAtUtc);

        return Result.Success(dto);
    }
}
