using Chat.Application.Abstractions;
using Chat.Application.Direct;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Domain.Identity;
using MediatR;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateGroupConversation;

public sealed class CreateGroupConversationCommandHandler
    : IRequestHandler<CreateGroupConversationCommand, Result<Guid>>
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly DirectConversationService? _reach;
    private readonly IUserDirectory _userDirectory;

    public const int MaxTitleLength = 120;
    public const int MaxParticipants = 200;

    public CreateGroupConversationCommandHandler(
        IChatDbContext db,
        ICurrentUserContext currentUser,
        IUserDirectory userDirectory,
        DirectConversationService? reach = null)
    {
        _db = db;
        _currentUser = currentUser;
        _userDirectory = userDirectory;
        _reach = reach;
    }

    public async Task<Result<Guid>> Handle(
        CreateGroupConversationCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } meId || _currentUser.TenantId is not { } tenantId)
            return Result.Failure<Guid>(Error.Unauthorized());

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
            return Result.Failure<Guid>(Error.Validation($"The group title is required ({MaxTitleLength} characters at most)."));

        var participantIds = (request.ParticipantIds ?? []).Where(id => id != Guid.Empty && id != meId).Distinct().ToList();
        if (participantIds.Count == 0 || participantIds.Count > MaxParticipants)
            return Result.Failure<Guid>(Error.Validation($"Choose between 1 and {MaxParticipants} other participants."));

        // Everyone in the group must exist, be enabled and belong to the creator's organization;
        // a person is never put into a conversation by someone who could not see them.
        var people = await _userDirectory.GetUsersAsync(participantIds, cancellationToken);
        if (people.Count != participantIds.Count || people.Any(person => !person.IsActive || person.TenantId != tenantId))
            return Result.Failure<Guid>(Error.NotFound("User was not found."));

        // Kept to one's own company (see IUserReachScope): only its people can be in the group.
        if (_reach is not null && !await _reach.CanReachAsync(participantIds, cancellationToken))
            return Result.Failure<Guid>(Error.NotFound("User was not found."));

        var conversation = new Conversation(
            Guid.NewGuid(),
            _currentUser.TenantId,
            title,
            ChatType.Group,
            meId);
        _db.Conversations.Add(conversation);
        _db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = meId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            });

        // Only the creator administers the group.
        foreach (var userId in participantIds)
        {
            _db.ConversationParticipants.AddRange(
                new ConversationParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow,
                    IsAdmin = false
                });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(conversation.Id);
    }
}