using MediatR;
using NexusCore.SharedKernel.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Tickets.Commands.CreateTicket;

public sealed record CreateTicketCommand(
    string Title,
    string Description,
    TicketPriority Priority
) : IRequest<Result<Guid>>;