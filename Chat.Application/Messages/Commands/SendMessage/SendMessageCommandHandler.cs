using Chat.Application.Abstractions;
using Chat.Application.Teams;
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
    private readonly TeamConversationService _teams;

    public SendMessageCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser,
        TeamConversationService teams)
    {
        _db = db;
        _currentUser = currentUser;
        _teams = teams;
    }

    public async Task<Result<Guid>> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000)
            return Result.Failure<Guid>(Error.Validation("A message needs 1-4000 characters of text."));

        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(x =>
                x.ConversationId == request.ConversationId &&
                x.UserId == _currentUser.UserId,
                cancellationToken);

        if (!isParticipant
            || !await _teams.IsCurrentMemberAsync(request.ConversationId, _currentUser.UserId!.Value, cancellationToken))
            return Result.Failure<Guid>(Error.NotFound("Conversation not found."));

        var message = new Message(
            Guid.NewGuid(),
            request.ConversationId,
            _currentUser.UserId,
            request.Text.Trim());

        _db.Messages.Add(message);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(message.Id);
    }
}