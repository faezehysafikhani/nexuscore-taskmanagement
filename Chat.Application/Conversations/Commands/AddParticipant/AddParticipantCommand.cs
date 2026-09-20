using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.AddParticipant;

public sealed record AddParticipantCommand(
    Guid ConversationId,
    Guid UserId
) : IRequest<Result>;