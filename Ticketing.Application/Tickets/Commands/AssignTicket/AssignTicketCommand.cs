using MediatR;
using NexusCore.SharedKernel.Results;

namespace Ticketing.Application.Tickets.Commands.AssignTicket;

public sealed record AssignTicketCommand(
    Guid TicketId,
    Guid UserId
) : IRequest<Result>;