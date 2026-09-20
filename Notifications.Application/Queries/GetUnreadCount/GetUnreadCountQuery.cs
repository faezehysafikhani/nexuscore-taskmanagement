using MediatR;
using NexusCore.SharedKernel.Results;

namespace Notifications.Application.Queries.GetUnreadCount;

public sealed record GetUnreadCountQuery()
    : IRequest<Result<int>>;