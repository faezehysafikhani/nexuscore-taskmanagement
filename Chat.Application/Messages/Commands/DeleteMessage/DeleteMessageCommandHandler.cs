using Chat.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.DeleteMessage;

public sealed class DeleteMessageCommandHandler
    : IRequestHandler<DeleteMessageCommand, Result>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public DeleteMessageCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        DeleteMessageCommand request,
        CancellationToken cancellationToken)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(x =>
                x.Id == request.MessageId &&
                x.SenderUserId == _currentUser.UserId,
                cancellationToken);

        if (message is null)
            return Result.Failure(Error.NotFound("Message not found"));

        message.Delete();

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}