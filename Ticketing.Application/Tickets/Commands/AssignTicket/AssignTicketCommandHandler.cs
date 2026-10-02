using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Common.Security;

namespace Ticketing.Application.Tickets.Commands.AssignTicket;

public class AssignTicketCommandHandler
    : IRequestHandler<AssignTicketCommand, Result>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IUserDirectory _users;

    public AssignTicketCommandHandler(ITicketingDbContext db, ICurrentUserContext currentUser, IUserDirectory users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result> Handle(
        AssignTicketCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .VisibleTo(_currentUser)
            .FirstOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken);

        if (ticket is null)
            return Result.Failure(new Error("tickets.not_found", "تیکت یافت نشد"));

        // Only to an active user of the ticket's own organization.
        var assignee = (await _users.GetUsersAsync([request.UserId], cancellationToken)).SingleOrDefault();
        if (assignee is null || !assignee.IsActive || assignee.TenantId != ticket.TenantId)
            return Result.Failure(Error.Validation("The assignee must be an active user of the same organization."));

        ticket.Assign(request.UserId);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}