using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Abstractions;
using Notifications.Application.Common.Dtos;

namespace Notifications.Application.Queries.GetMyNotifications;

public sealed class GetMyNotificationsQueryHandler
    : IRequestHandler<
        GetMyNotificationsQuery,
        Result<List<NotificationDto>>>
{
    private readonly INotificationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetMyNotificationsQueryHandler(
        INotificationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<NotificationDto>>> Handle(
        GetMyNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.Notifications
            .Where(n => n.UserId == _currentUser.UserId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(
                n.Id,
                n.Title,
                n.Message,
                n.Type,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(notifications);
    }
}