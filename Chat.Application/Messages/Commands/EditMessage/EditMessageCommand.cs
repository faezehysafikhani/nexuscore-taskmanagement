using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.EditMessage;

public sealed record EditMessageCommand(
    Guid MessageId,
    string Text
) : IRequest<Result>;