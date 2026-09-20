using Chat.Application.Abstractions;
using Chat.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.MarkAsRead;

public sealed class MarkAsReadCommandHandler
    : IRequestHandler<MarkAsReadCommand, Result>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public MarkAsReadCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        MarkAsReadCommand request,
        CancellationToken cancellationToken)
    {
        var exists = await _db.MessageReads
            .AnyAsync(x =>
                x.MessageId == request.MessageId &&
                x.UserId == _currentUser.UserId,
                cancellationToken);

        if (exists)
            return Result.Success();

        var read = new MessageRead(
            Guid.NewGuid(),
            request.MessageId,
            _currentUser.UserId);

        _db.MessageReads.Add(read);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}