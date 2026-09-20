using Chat.Application.Abstractions;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Domain.Identity;
using MediatR;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateDirectConversation;

public sealed class CreateDirectConversationCommandHandler
    : IRequestHandler<CreateDirectConversationCommand, Result<Guid>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CreateDirectConversationCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        CreateDirectConversationCommand request,
        CancellationToken cancellationToken)
    {
        var conversation = new Conversation(
            Guid.NewGuid(),
            _currentUser.TenantId,
            null,
            ChatType.Direct,
            _currentUser.UserId);

        _db.Conversations.Add(conversation);

        _db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = _currentUser.UserId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = request.OtherUserId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = false
            });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(conversation.Id);
    }
}