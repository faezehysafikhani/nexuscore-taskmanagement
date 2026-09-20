using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common.Dtos;

namespace Ticketing.Application.Tickets.Queries.GetMyTickets;

public class GetMyTicketsQueryHandler
    : IRequestHandler<GetMyTicketsQuery, Result<List<TicketDto>>>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetMyTicketsQueryHandler(
        ITicketingDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<TicketDto>>> Handle(
        GetMyTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var tickets = await _db.Tickets
            .Where(x => x.CreatedByUserId == _currentUser.UserId
                     || x.AssignedToUserId == _currentUser.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new TicketDto(
                x.Id,
                x.Number,
                x.Title,
                x.Status.ToString(),
                x.Priority.ToString(),
                x.AssignedToUserId,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(tickets);
    }
}