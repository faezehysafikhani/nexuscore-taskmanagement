using Events.Application.Abstractions;
using Events.Application.DTOs;
using Events.Domain.Entities;
using MediatR;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Events.Application.Commands.CreateEvent;

public record CreateEventCommand(
    string Title,
    string? Description,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    int? ReminderMinutesBefore) : IRequest<Result<EventDto>>;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, Result<EventDto>>
{
    private readonly IEventsDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CreateEventCommandHandler(IEventsDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<EventDto>> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || !_currentUser.TenantId.HasValue)
        {
            return Result.Failure<EventDto>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<EventDto>(Error.Validation("Event title is required."));
        }

        var calendarEvent = new CalendarEvent(
            Guid.NewGuid(),
            _currentUser.TenantId.Value,
            _currentUser.UserId.Value,
            request.Title.Trim(),
            request.Description?.Trim(),
            request.StartAtUtc,
            request.EndAtUtc,
            request.ReminderMinutesBefore);

        _db.Events.Add(calendarEvent);
        await _db.SaveChangesAsync(cancellationToken);

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
