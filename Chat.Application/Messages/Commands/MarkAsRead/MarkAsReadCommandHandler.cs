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
        // Only a participant of the message's conversation may mark it read.
        var visible = await _db.Messages
            .AnyAsync(m =>
                m.Id == request.MessageId &&
                !m.IsDeleted &&
                _db.ConversationParticipants.Any(p =>
                    p.ConversationId == m.ConversationId &&
                    p.UserId == _currentUser.UserId),
                cancellationToken);

        if (!visible)
            return Result.Failure(Error.NotFound("Message not found"));

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

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Marked by a parallel request in the meantime: it is read, which is all we wanted.
        }

        return Result.Success();
    }
}
