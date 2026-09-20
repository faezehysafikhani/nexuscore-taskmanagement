using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Abstractions;

namespace Notifications.Application.Queries.GetUnreadCount;

public sealed class GetUnreadCountQueryHandler
    : IRequestHandler<GetUnreadCountQuery, Result<int>>
{
    private readonly INotificationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetUnreadCountQueryHandler(
        INotificationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<int>> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        var count = await _db.Notifications
            .Where(n => n.UserId == _currentUser.UserId)
            .Where(n => !n.IsRead)
            .CountAsync(cancellationToken);

        return Result.Success(count);
    }
}