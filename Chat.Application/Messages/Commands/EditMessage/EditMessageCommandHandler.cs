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
        var message = await _db.Messages
            .FirstOrDefaultAsync(x =>
                x.Id == request.MessageId &&
                x.SenderUserId == _currentUser.UserId,
                cancellationToken);

        if (message is null)
            return Result.Failure(Error.NotFound("Message not found"));

        message.Edit(request.Text);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}