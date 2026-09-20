using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetUnreadCount;

public sealed record GetUnreadCountQuery()
    : IRequest<Result<int>>;