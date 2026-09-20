using MediatR;
using NexusCore.SharedKernel.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Tickets.Commands.ChangeStatus;

public sealed record ChangeStatusCommand(
    Guid TicketId,
    TicketStatus Status
) : IRequest<Result>;   