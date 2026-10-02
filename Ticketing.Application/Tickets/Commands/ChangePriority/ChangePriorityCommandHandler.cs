using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common.Security;

namespace Ticketing.Application.Tickets.Commands.ChangePriority;

public class ChangePriorityCommandHandler
    : IRequestHandler<ChangePriorityCommand, Result>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public ChangePriorityCommandHandler(ITicketingDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        ChangePriorityCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .VisibleTo(_currentUser)
            .FirstOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken);

        if (ticket is null)
            return Result.Failure(new Error("tickets.not_found", "تیکت یافت نشد"));

        ticket.ChangePriority(request.Priority);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}