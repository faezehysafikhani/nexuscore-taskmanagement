using Chat.Application.Common.Dtos;
using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetMyConversations;

public sealed record GetMyConversationsQuery()
    : IRequest<Result<List<ConversationDto>>>;