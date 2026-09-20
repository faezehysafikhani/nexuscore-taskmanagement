using Chat.Application.Abstractions;
using Chat.Application.Common.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetConversationMessages;

public sealed class GetConversationMessagesQueryHandler
    : IRequestHandler<GetConversationMessagesQuery, Result<List<MessageDto>>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetConversationMessagesQueryHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<MessageDto>>> Handle(
        GetConversationMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(x =>
                x.ConversationId == request.ConversationId &&
                x.UserId == _currentUser.UserId,
                cancellationToken);

        if (!isParticipant)
            return Result.Failure<List<MessageDto>>(Error.Unauthorized("Access denied"));

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == request.ConversationId &&
                !m.IsDeleted)
            .OrderByDescending(m => m.SentAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MessageDto(
                m.Id,
                m.SenderUserId ?? Guid.Empty,
                m.Text,
                m.SentAt,
                m.SenderUserId == _currentUser.UserId,
                _db.MessageReads.Any(r =>
                    r.MessageId == m.Id &&
                    r.UserId == _currentUser.UserId)
            ))
            .ToListAsync(cancellationToken);

        messages.Reverse();

        return Result.Success(messages);
    }
}