using MediatR;
using NexusCore.SharedKernel.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Tickets.Commands.ChangePriority;

public sealed record ChangePriorityCommand(
    Guid TicketId,
    TicketPriority Priority
) : IRequest<Result>;