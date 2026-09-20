using Chat.Application.Common.Dtos;
using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetConversationMessages;

public sealed record GetConversationMessagesQuery(
    Guid ConversationId,
    int Page = 1,
    int PageSize = 50
) : IRequest<Result<List<MessageDto>>>;