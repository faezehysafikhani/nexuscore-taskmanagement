using MediatR;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Common.Dtos;

namespace Ticketing.Application.Tickets.Queries.GetMyTickets;

public sealed record GetMyTicketsQuery()
    : IRequest<Result<List<TicketDto>>>;