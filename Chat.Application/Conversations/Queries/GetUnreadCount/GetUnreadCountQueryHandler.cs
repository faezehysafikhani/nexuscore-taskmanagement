using Chat.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetUnreadCount;

public sealed class GetUnreadCountQueryHandler
    : IRequestHandler<GetUnreadCountQuery, Result<int>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetUnreadCountQueryHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<int>> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        var count = await _db.Messages
            .Where(m => !m.IsDeleted)
            .Where(m => m.SenderUserId != _currentUser.UserId)
            .Where(m => _db.ConversationParticipants
                .Any(p =>
                    p.ConversationId == m.ConversationId &&
                    p.UserId == _currentUser.UserId))
            .Where(m => !_db.MessageReads
                .Any(r =>
                    r.MessageId == m.Id &&
                    r.UserId == _currentUser.UserId))
            .CountAsync(cancellationToken);

        return Result.Success(count);
    }
}