using Events.Application.Abstractions;
using Events.Application.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Events.Application.Commands.UpdateEvent;

public record UpdateEventCommand(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    bool IsCompleted,
    int? ReminderMinutesBefore) : IRequest<Result<EventDto>>;

public class UpdateEventCommandHandler : IRequestHandler<UpdateEventCommand, Result<EventDto>>
{
    private readonly IEventsDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public UpdateEventCommandHandler(IEventsDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<EventDto>> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || !_currentUser.TenantId.HasValue)
        {
            return Result.Failure<EventDto>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<EventDto>(Error.Validation("Event title is required."));
        }

        var calendarEvent = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.TenantId == _currentUser.TenantId.Value && e.UserId == _currentUser.UserId.Value, cancellationToken);

        if (calendarEvent is null)
        {
            return Result.Failure<EventDto>(Error.NotFound("Event not found."));
        }

        calendarEvent.Update(
            request.Title.Trim(),
            request.Description?.Trim(),
            request.StartAtUtc,
            request.EndAtUtc,
            request.IsCompleted,
            request.ReminderMinutesBefore);

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
