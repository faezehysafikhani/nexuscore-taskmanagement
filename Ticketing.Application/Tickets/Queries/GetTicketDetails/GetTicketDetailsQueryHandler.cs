using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common.Dtos;

namespace Ticketing.Application.Tickets.Queries.GetTicketDetails;

public class GetTicketDetailsQueryHandler
    : IRequestHandler<GetTicketDetailsQuery, Result<TicketDetailsDto>>
{
    private readonly ITicketingDbContext _db;

    public GetTicketDetailsQueryHandler(ITicketingDbContext db)
    {
        _db = db;
    }

    public async Task<Result<TicketDetailsDto>> Handle(
        GetTicketDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken);

        if (ticket is null)
            return Result.Failure<TicketDetailsDto>(
                new Error("tickets.not_found", "تیکت یافت نشد"));

        var comments = await _db.TicketComments
            .Where(x => x.TicketId == request.TicketId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new TicketCommentDto(
                x.Id,
                x.UserId,
                x.Text,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new TicketDetailsDto(
            ticket.Id,
            ticket.Number,
            ticket.Title,
            ticket.Description,
            ticket.Status.ToString(),
            ticket.Priority.ToString(),
            ticket.CreatedByUserId,
            ticket.AssignedToUserId,
            ticket.CreatedAtUtc,
            comments));
    }
}