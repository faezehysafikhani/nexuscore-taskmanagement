using Chat.Application.Abstractions;
using Chat.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.AddParticipant;

public sealed class AddParticipantCommandHandler
    : IRequestHandler<AddParticipantCommand, Result>
{
    private readonly IChatDbContext _db;

    public AddParticipantCommandHandler(IChatDbContext db)
    {
        _db = db;
    }

    public async Task<Result> Handle(
        AddParticipantCommand request,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(x => x.Id == request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(Error.NotFound("Conversation not found"));

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