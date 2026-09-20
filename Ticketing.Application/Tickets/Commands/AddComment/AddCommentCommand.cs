using MediatR;
using NexusCore.SharedKernel.Results;

namespace Ticketing.Application.Tickets.Commands.AddComment;

public sealed record AddCommentCommand(
    Guid TicketId,
    string Text
) : IRequest<Result<Guid>>;