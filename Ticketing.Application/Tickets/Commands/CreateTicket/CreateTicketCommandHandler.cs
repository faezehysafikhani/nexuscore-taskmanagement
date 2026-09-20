using MediatR;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Tickets.Commands.CreateTicket;

public class CreateTicketCommandHandler
    : IRequestHandler<CreateTicketCommand, Result<Guid>>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CreateTicketCommandHandler(
        ITicketingDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        CreateTicketCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = new Ticket(
            Guid.NewGuid(),
            _currentUser.TenantId,
            request.Title,
            request.Description,
            request.Priority,
            _currentUser.UserId);

        ticket.SetNumber($"TCK-{DateTime.UtcNow:yyyyMMddHHmmss}");

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ticket.Id);
    }
}