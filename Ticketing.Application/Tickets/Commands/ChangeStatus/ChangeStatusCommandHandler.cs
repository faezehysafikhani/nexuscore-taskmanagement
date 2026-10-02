using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common.Security;

namespace Ticketing.Application.Tickets.Commands.ChangeStatus;

public class ChangeStatusCommandHandler
    : IRequestHandler<ChangeStatusCommand, Result>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public ChangeStatusCommandHandler(ITicketingDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        ChangeStatusCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .VisibleTo(_currentUser)
            .FirstOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken);

        if (ticket is null)
            return Result.Failure(new Error("tickets.not_found", "تیکت یافت نشد"));

        // The person working the ticket moves its status; otherwise it takes Tickets.Manage.
        if (ticket.AssignedToUserId != _currentUser.UserId && !_currentUser.HasPermission(TicketingPermissions.Manage))
            return Result.Failure(Error.Forbidden("Only the assignee or a ticket manager can change the status."));

        ticket.ChangeStatus(request.Status);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}