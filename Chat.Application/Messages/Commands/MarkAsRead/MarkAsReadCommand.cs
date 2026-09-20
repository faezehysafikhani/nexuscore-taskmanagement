using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.MarkAsRead;

public sealed record MarkAsReadCommand(
    Guid MessageId
) : IRequest<Result>;