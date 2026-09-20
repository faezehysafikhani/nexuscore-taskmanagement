using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateDirectConversation;

public sealed record CreateDirectConversationCommand(
    Guid OtherUserId
) : IRequest<Result<Guid>>;