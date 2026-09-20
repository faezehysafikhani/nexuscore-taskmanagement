using Events.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Events.Application.Commands.DeleteEvent;

public record DeleteEventCommand(Guid Id) : IRequest<Result>;

public class DeleteEventCommandHandler : IRequestHandler<DeleteEventCommand, Result>
{
    private readonly IEventsDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public DeleteEventCommandHandler(IEventsDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || !_currentUser.TenantId.HasValue)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var calendarEvent = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.TenantId == _currentUser.TenantId.Value && e.UserId == _currentUser.UserId.Value, cancellationToken);

        if (calendarEvent is null)
        {
            return Result.Failure(Error.NotFound("Event not found."));
        }

        _db.Events.Remove(calendarEvent);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
