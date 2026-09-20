using Events.Application.Abstractions;
using Events.Application.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Events.Application.Queries.GetMyEvents;

public record GetMyEventsQuery(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    bool? IsCompleted = null) : IRequest<Result<List<EventDto>>>;

public class GetMyEventsQueryHandler : IRequestHandler<GetMyEventsQuery, Result<List<EventDto>>>
{
    private readonly IEventsDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetMyEventsQueryHandler(IEventsDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<EventDto>>> Handle(GetMyEventsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || !_currentUser.TenantId.HasValue)
        {
            return Result.Failure<List<EventDto>>(Error.Unauthorized());
        }

        var query = _db.Events
            .AsNoTracking()
            .Where(e => e.TenantId == _currentUser.TenantId.Value && e.UserId == _currentUser.UserId.Value);

        if (request.StartDate.HasValue)
        {
            query = query.Where(e => e.StartAtUtc >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(e => e.StartAtUtc <= request.EndDate.Value);
        }

        if (request.IsCompleted.HasValue)
        {
            query = query.Where(e => e.IsCompleted == request.IsCompleted.Value);
        }

        var events = await query
            .OrderBy(e => e.StartAtUtc)
            .Select(e => new EventDto(
                e.Id,
                e.TenantId,
                e.UserId,
                e.Title,
                e.Description,
                e.StartAtUtc,
                e.EndAtUtc,
                e.IsCompleted,
                e.ReminderMinutesBefore,
                e.ReminderSent,
                e.CreatedAtUtc,
                e.ModifiedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(events);
    }
}
