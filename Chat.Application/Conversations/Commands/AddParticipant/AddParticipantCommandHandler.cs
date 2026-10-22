using Chat.Application.Abstractions;
using Chat.Application.Direct;
using Chat.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.AddParticipant;

public sealed class AddParticipantCommandHandler
    : IRequestHandler<AddParticipantCommand, Result>
{
    private readonly IChatDbContext _db;
    private readonly DirectConversationService? _reach;

    public AddParticipantCommandHandler(IChatDbContext db, DirectConversationService? reach = null)
    {
        _db = db;
        _reach = reach;
    }

    public async Task<Result> Handle(
        AddParticipantCommand request,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(x => x.Id == request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(Error.NotFound("Conversation not found"));

        // Kept to one's own company (see IUserReachScope): only its people can be added.
        if (_reach is not null && !await _reach.CanReachAsync([request.UserId], cancellationToken))
            return Result.Failure(Error.NotFound("User was not found."));

        _db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = request.UserId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}