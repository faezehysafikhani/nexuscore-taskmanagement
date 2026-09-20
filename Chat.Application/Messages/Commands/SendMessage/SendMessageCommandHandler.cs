using Chat.Application.Abstractions;
using Chat.Domain.Entities;
using Chat.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Messages.Commands.SendMessage;

public sealed class SendMessageCommandHandler
    : IRequestHandler<SendMessageCommand, Result<Guid>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public SendMessageCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(x =>
                x.ConversationId == request.ConversationId &&
                x.UserId == _currentUser.UserId,
                cancellationToken);

        if (!isParticipant)
            return Result.Failure<Guid>(Error.Validation("You are not a participant of this conversation"));

        var message = new Message(
            Guid.NewGuid(),
            request.ConversationId,
            _currentUser.UserId,
            request.Text);

        _db.Messages.Add(message);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(message.Id);
    }
}