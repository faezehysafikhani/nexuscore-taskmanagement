using Chat.Application.Abstractions;
using Chat.Application.Common.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Queries.GetMyConversations;

public sealed class GetMyConversationsQueryHandler
    : IRequestHandler<GetMyConversationsQuery, Result<List<ConversationDto>>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetMyConversationsQueryHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<ConversationDto>>> Handle(
    GetMyConversationsQuery request,
    CancellationToken cancellationToken)
    {
        var conversations = await _db.Conversations
            .Where(c => c.TenantId == _currentUser.TenantId)
            .Where(c => _db.ConversationParticipants
                .Any(p =>
                    p.ConversationId == c.Id &&
                    p.UserId == _currentUser.UserId))
            .Select(c => new ConversationDto(
                c.Id,
                c.Title,
                c.Type.ToString(),
                c.CreatedAt,

                _db.ConversationParticipants
                    .Count(p => p.ConversationId == c.Id),

                _db.Messages
                    .Where(m =>
                        m.ConversationId == c.Id &&
                        !m.IsDeleted)
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => m.Text)
                    .FirstOrDefault(),

                _db.Messages
                    .Where(m =>
                        m.ConversationId == c.Id &&
                        !m.IsDeleted)
                    .Max(m => (DateTime?)m.SentAt)
            ))
            .ToListAsync(cancellationToken);

        // این قسمت خارج از Query دیتابیس اجرا می‌شود
        conversations = conversations
            .OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt)
            .ToList();

        return Result.Success(conversations);
    }
}