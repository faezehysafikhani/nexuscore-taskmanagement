using Chat.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.EditMessage;

public sealed class EditMessageCommandHandler
    : IRequestHandler<EditMessageCommand, Result>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public EditMessageCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        EditMessageCommand request,
        CancellationToken cancellationToken)
    {
        // Only the sender can edit, and only a message that still exists.
        var message = await _db.Messages
            .FirstOrDefaultAsync(x =>
                x.Id == request.MessageId &&
                x.SenderUserId == _currentUser.UserId,
                cancellationToken);

        if (message is null || message.IsDeleted)
            return Result.Failure(Error.NotFound("Message not found"));

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000)
            return Result.Failure(Error.Validation("A message needs 1-4000 characters of text."));

        message.Edit(request.Text.Trim());

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
