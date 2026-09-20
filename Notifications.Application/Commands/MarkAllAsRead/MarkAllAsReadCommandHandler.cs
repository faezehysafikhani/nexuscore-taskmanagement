using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Abstractions;

namespace Notifications.Application.Commands.MarkAllAsRead;

public sealed class MarkAllAsReadCommandHandler
    : IRequestHandler<MarkAllAsReadCommand, Result>
{
    private readonly INotificationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public MarkAllAsReadCommandHandler(
        INotificationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        MarkAllAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var unread = await _db.Notifications
            .Where(n => n.UserId == _currentUser.UserId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
            return Result.Success();

        foreach (var notification in unread)
        {
            notification.MarkAsRead();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
