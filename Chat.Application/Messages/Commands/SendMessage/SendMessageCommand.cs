using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.SendMessage;

public sealed record SendMessageCommand(
    Guid ConversationId,
    string Text,
    Guid? SenderUserId
) : IRequest<Result<Guid>>;