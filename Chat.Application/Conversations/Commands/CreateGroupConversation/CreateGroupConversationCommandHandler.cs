using Chat.Application.Abstractions;
using Chat.Application.Direct;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Domain.Identity;
using MediatR;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateGroupConversation;

public sealed class CreateGroupConversationCommandHandler
    : IRequestHandler<CreateGroupConversationCommand, Result<Guid>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly DirectConversationService? _reach;

    public CreateGroupConversationCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser,
        DirectConversationService? reach = null)
    {
        _db = db;
        _currentUser = currentUser;
        _reach = reach;
    }

    public async Task<Result<Guid>> Handle(
        CreateGroupConversationCommand request,
        CancellationToken cancellationToken)
    {
        // Kept to one's own company (see IUserReachScope): only its people can be in the group.
        if (_reach is not null && !await _reach.CanReachAsync(request.ParticipantIds, cancellationToken))
            return Result.Failure<Guid>(Error.NotFound("User was not found."));

        var conversation = new Conversation(
            Guid.NewGuid(),
            _currentUser.TenantId,
            request.Title,
            ChatType.Group,
            _currentUser.UserId);
        _db.Conversations.Add(conversation);
        _db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = _currentUser.UserId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            });

        foreach (var userId in request.ParticipantIds.Distinct())
        {
            if (userId != _currentUser.UserId)
                _db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = userId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(conversation.Id);
    }
}