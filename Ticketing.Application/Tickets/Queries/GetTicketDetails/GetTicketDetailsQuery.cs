using MediatR;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Common.Dtos;

namespace Ticketing.Application.Tickets.Queries.GetTicketDetails;

public sealed record GetTicketDetailsQuery(Guid TicketId)
    : IRequest<Result<TicketDetailsDto>>;