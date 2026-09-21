using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.SendMessage;

public sealed record SendMessageCommand(
    Guid ConversationId,
    string Text
) : IRequest<Result<Guid>>;
// The sender is not part of the command: it is always the signed-in user.