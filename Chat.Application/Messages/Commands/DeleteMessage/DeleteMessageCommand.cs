using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.DeleteMessage;

public sealed record DeleteMessageCommand(
    Guid MessageId
) : IRequest<Result>;