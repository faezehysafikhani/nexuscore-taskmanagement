using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;

namespace Ticketing.Application.Tickets.Commands.AssignTicket;

public class AssignTicketCommandHandler
    : IRequestHandler<AssignTicketCommand, Result>
{
    private readonly ITicketingDbContext _db;

    public AssignTicketCommandHandler(ITicketingDbContext db)
    {
        _db = db;
    }

    public async Task<Result> Handle(
        AssignTicketCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken);

        if (ticket is null)
            return Result.Failure(new Error("tickets.not_found", "تیکت یافت نشد"));

        ticket.Assign(request.UserId);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}