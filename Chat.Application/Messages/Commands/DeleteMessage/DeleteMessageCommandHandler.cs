using Chat.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.DeleteMessage;

public sealed class DeleteMessageCommandHandler
    : IRequestHandler<DeleteMessageCommand, Result>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IFileStorage _fileStorage;

    public DeleteMessageCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser,
        IFileStorage fileStorage)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(
        DeleteMessageCommand request,
        CancellationToken cancellationToken)
    {
        // Only the sender can delete their message.
        var message = await _db.Messages
            .FirstOrDefaultAsync(x =>
                x.Id == request.MessageId &&
                x.SenderUserId == _currentUser.UserId,
                cancellationToken);

        if (message is null || message.IsDeleted)
            return Result.Failure(Error.NotFound("Message not found"));

        var attachmentKey = message.AttachmentStorageKey;

        message.Delete();

        await _db.SaveChangesAsync(cancellationToken);

        // The attached file goes too; a deleted message must not leave its content readable.
        if (attachmentKey is not null)
            await _fileStorage.DeleteAsync(attachmentKey, cancellationToken);

        return Result.Success();
    }
}
