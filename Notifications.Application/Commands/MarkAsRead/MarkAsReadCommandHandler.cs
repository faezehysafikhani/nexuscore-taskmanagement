using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Abstractions;

namespace Notifications.Application.Commands.MarkAsRead;

public sealed class MarkAsReadCommandHandler
    : IRequestHandler<MarkAsReadCommand, Result>
{
    private readonly INotificationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public MarkAsReadCommandHandler(
        INotificationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        MarkAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var notification = await _db.Notifications
            .SingleOrDefaultAsync(
                n => n.Id == request.NotificationId &&
                     n.UserId == _currentUser.UserId,
                cancellationToken);

        if (notification is null)
            return Result.Failure(Error.NotFound("Notification was not found."));

        if (!notification.IsRead)
        {
            notification.MarkAsRead();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
