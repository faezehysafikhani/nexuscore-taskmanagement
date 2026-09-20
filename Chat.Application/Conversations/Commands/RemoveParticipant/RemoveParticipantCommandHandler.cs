using Chat.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.RemoveParticipant;

public sealed class RemoveParticipantCommandHandler
    : IRequestHandler<RemoveParticipantCommand, Result>
{
    private readonly IChatDbContext _db;

    public RemoveParticipantCommandHandler(IChatDbContext db)
    {
        _db = db;
    }

    public async Task<Result> Handle(
        RemoveParticipantCommand request,
        CancellationToken cancellationToken)
    {
        var participant = await _db.ConversationParticipants
            .FirstOrDefaultAsync(x =>
                x.ConversationId == request.ConversationId &&
                x.UserId == request.UserId,
                cancellationToken);

        if (participant is null)
            return Result.Failure(Error.NotFound("Participant not found"));

        _db.ConversationParticipants.Remove(participant);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}