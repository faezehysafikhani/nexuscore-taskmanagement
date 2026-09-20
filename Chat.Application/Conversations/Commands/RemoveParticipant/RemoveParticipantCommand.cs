using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.RemoveParticipant;

public sealed record RemoveParticipantCommand(
    Guid ConversationId,
    Guid UserId
) : IRequest<Result>;