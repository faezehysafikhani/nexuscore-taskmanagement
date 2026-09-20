using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateGroupConversation;

public sealed record CreateGroupConversationCommand(
    string Title,
    List<Guid> ParticipantIds
) : IRequest<Result<Guid>>;