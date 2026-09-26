using Chat.Application.Abstractions;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexusCore.Application.Files;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Direct;

/// <summary>
/// Shared rules for conversations between two users: who may talk to whom, and how the one
/// conversation of a pair is found or created.
/// </summary>
public sealed class DirectConversationService(
    IChatDbContext db,
    ICurrentUserContext currentUser,
    IUserDirectory userDirectory)
{
    public const int MaxTextLength = 4000;

    /// <summary>The signed-in user and the partner, checked: both exist, are active, share a tenant and may talk.</summary>
    public async Task<Result<(UserContact Me, UserContact Other)>> ResolvePairAsync(Guid otherUserId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } meId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<(UserContact, UserContact)>(Error.Unauthorized());
        }

        if (otherUserId == meId)
        {
            return Result.Failure<(UserContact, UserContact)>(Error.Validation("You cannot start a conversation with yourself."));
        }

        var users = await userDirectory.GetUsersAsync([meId, otherUserId], cancellationToken);
        var me = users.FirstOrDefault(user => user.Id == meId);
        var other = users.FirstOrDefault(user => user.Id == otherUserId);

        if (me is null || other is null || other.TenantId != tenantId || !other.IsActive)
        {
            return Result.Failure<(UserContact, UserContact)>(Error.NotFound("User was not found."));
        }

        if (!currentUser.HasPermission(UserGroupPermissions.GroupsManageMembers)
            && !await ShareAnyWorkTeamAsync(meId, otherUserId, cancellationToken))
        {
            return Result.Failure<(UserContact, UserContact)>(Error.NotFound("User was not found."));
        }

        return Result.Success((me, other));
    }

    public Task<Conversation?> FindAsync(Guid meId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var key = Conversation.BuildDirectKey(meId, otherUserId);
        return db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == key && c.TenantId == currentUser.TenantId, cancellationToken);
    }

    public async Task<Conversation> FindOrCreateAsync(Guid meId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(meId, otherUserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var conversation = Conversation.CreateDirect(Guid.NewGuid(), currentUser.TenantId, meId, otherUserId);
        db.Conversations.Add(conversation);
        db.ConversationParticipants.AddRange(
            new ConversationParticipant { ConversationId = conversation.Id, UserId = meId, JoinedAt = DateTime.UtcNow, IsAdmin = true },
            new ConversationParticipant { ConversationId = conversation.Id, UserId = otherUserId, JoinedAt = DateTime.UtcNow, IsAdmin = false });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return conversation;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await FindAsync(meId, otherUserId, cancellationToken)
                   ?? throw new InvalidOperationException("The direct conversation could not be created.");
        }
    }

    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DirectMessageDto ToDto(Message message, UserContact sender, Guid receiverId, bool isRead) =>
        new(
            message.Id,
            message.ConversationId,
            sender.Id,
            receiverId,
            sender.DisplayName,
            sender.AvatarUrl,
            message.Text,
            Utc(message.SentAt),
            message.EditedAt is { } edited ? Utc(edited) : null,
            isRead,
            message.HasAttachment
                ? new ChatAttachmentDto(message.AttachmentFileName!, message.AttachmentContentType ?? "application/octet-stream", message.AttachmentSizeBytes ?? 0)
                : null);

    private async Task<bool> ShareAnyWorkTeamAsync(Guid meId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var myGroups = await userDirectory.GetGroupIdsOfUserAsync(meId, cancellationToken);
        if (myGroups.Count == 0)
        {
            return false;
        }

        var otherGroups = await userDirectory.GetGroupIdsOfUserAsync(otherUserId, cancellationToken);
        return myGroups.Intersect(otherGroups).Any();
    }
}

public sealed class GetDirectMessagesQueryHandler(
    IChatDbContext db,
    DirectConversationService conversations)
    : IRequestHandler<GetDirectMessagesQuery, Result<List<DirectMessageDto>>>
{
    public async Task<Result<List<DirectMessageDto>>> Handle(GetDirectMessagesQuery request, CancellationToken cancellationToken)
    {
        var pair = await conversations.ResolvePairAsync(request.OtherUserId, cancellationToken);
        if (pair.IsFailure)
        {
            return Result.Failure<List<DirectMessageDto>>(pair.Error);
        }

        var (me, other) = pair.Value;
        var conversation = await conversations.FindAsync(me.Id, other.Id, cancellationToken);
        if (conversation is null)
        {
            return Result.Success(new List<DirectMessageDto>());
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 1000);

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && !m.IsDeleted)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        messages.Reverse();

        var ids = messages.Select(m => m.Id).ToList();
        var reads = await db.MessageReads
            .AsNoTracking()
            .Where(r => r.MessageId != null && ids.Contains(r.MessageId.Value))
            .Select(r => new { r.MessageId, r.UserId })
            .ToListAsync(cancellationToken);
        var readPairs = reads.Select(r => (r.MessageId!.Value, r.UserId)).ToHashSet();

        return Result.Success(messages.Select(m =>
        {
            var fromMe = m.SenderUserId == me.Id;
            var receiverId = fromMe ? other.Id : me.Id;
            return DirectConversationService.ToDto(m, fromMe ? me : other, receiverId, readPairs.Contains((m.Id, receiverId)));
        }).ToList());
    }
}

public sealed class SendDirectMessageCommandHandler(
    IChatDbContext db,
    DirectConversationService conversations,
    IFileStorage fileStorage,
    IOptions<ChatOptions> options)
    : IRequestHandler<SendDirectMessageCommand, Result<DirectMessageDto>>
{
    public async Task<Result<DirectMessageDto>> Handle(SendDirectMessageCommand request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && request.Attachment is null)
        {
            return Result.Failure<DirectMessageDto>(Error.Validation("A message needs text or an attachment."));
        }

        if (text.Length > DirectConversationService.MaxTextLength)
        {
            return Result.Failure<DirectMessageDto>(Error.Validation($"A message can be at most {DirectConversationService.MaxTextLength} characters."));
        }

        if (request.Attachment is { } upload)
        {
            if (upload.Length <= 0)
            {
                return Result.Failure<DirectMessageDto>(Error.Validation("The attached file is empty."));
            }

            if (upload.Length > options.Value.MaxAttachmentBytes)
            {
                return Result.Failure<DirectMessageDto>(Error.Validation(
                    $"The attached file is too large. The limit is {options.Value.MaxAttachmentBytes / (1024 * 1024)} MB."));
            }
        }

        var pair = await conversations.ResolvePairAsync(request.OtherUserId, cancellationToken);
        if (pair.IsFailure)
        {
            return Result.Failure<DirectMessageDto>(pair.Error);
        }

        var (me, other) = pair.Value;
        var conversation = await conversations.FindOrCreateAsync(me.Id, other.Id, cancellationToken);
        var message = new Message(Guid.NewGuid(), conversation.Id, me.Id, text);

        if (request.Attachment is { } attachment)
        {
            var fileName = Path.GetFileName(attachment.FileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "attachment";
            }

            var contentType = string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType;
            var stored = await fileStorage.SaveAsync(fileName, contentType, attachment.Content, cancellationToken);
            message.AttachFile(fileName.Length > 260 ? fileName[..260] : fileName, contentType, stored.SizeBytes, stored.StorageKey);
        }

        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(DirectConversationService.ToDto(message, me, other.Id, isRead: false));
    }
}

public sealed class MarkDirectConversationReadCommandHandler(
    IChatDbContext db,
    DirectConversationService conversations)
    : IRequestHandler<MarkDirectConversationReadCommand, Result<int>>
{
    public async Task<Result<int>> Handle(MarkDirectConversationReadCommand request, CancellationToken cancellationToken)
    {
        var pair = await conversations.ResolvePairAsync(request.OtherUserId, cancellationToken);
        if (pair.IsFailure)
        {
            return Result.Failure<int>(pair.Error);
        }

        var (me, other) = pair.Value;
        var conversation = await conversations.FindAsync(me.Id, other.Id, cancellationToken);
        if (conversation is null)
        {
            return Result.Success(0);
        }

        var unreadIds = await db.Messages
            .Where(m => m.ConversationId == conversation.Id && !m.IsDeleted && m.SenderUserId == other.Id)
            .Where(m => !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == me.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (unreadIds.Count == 0)
        {
            return Result.Success(0);
        }

        db.MessageReads.AddRange(unreadIds.Select(id => new MessageRead(Guid.NewGuid(), id, me.Id)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Success(0);
        }

        return Result.Success(unreadIds.Count);
    }
}

public sealed class GetUnreadCountsBySenderQueryHandler(
    IChatDbContext db,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetUnreadCountsBySenderQuery, Result<List<UnreadBySenderDto>>>
{
    public async Task<Result<List<UnreadBySenderDto>>> Handle(GetUnreadCountsBySenderQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } meId)
        {
            return Result.Failure<List<UnreadBySenderDto>>(Error.Unauthorized());
        }

        var counts = await db.Messages
            .AsNoTracking()
            .Where(m => !m.IsDeleted && m.SenderUserId != null && m.SenderUserId != meId)
            .Where(m => db.Conversations.Any(c => c.Id == m.ConversationId && c.Type == ChatType.Direct && c.TenantId == currentUser.TenantId))
            .Where(m => db.ConversationParticipants.Any(p => p.ConversationId == m.ConversationId && p.UserId == meId))
            .Where(m => !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == meId))
            .GroupBy(m => m.SenderUserId!.Value)
            .Select(g => new UnreadBySenderDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return Result.Success(counts);
    }
}

public sealed class GetMessageAttachmentQueryHandler(
    IChatDbContext db,
    ICurrentUserContext currentUser,
    IFileStorage fileStorage,
    Chat.Application.Teams.TeamConversationService teams)
    : IRequestHandler<GetMessageAttachmentQuery, Result<ChatAttachmentDownload>>
{
    public async Task<Result<ChatAttachmentDownload>> Handle(GetMessageAttachmentQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } meId)
        {
            return Result.Failure<ChatAttachmentDownload>(Error.Unauthorized());
        }

        var message = await db.Messages
            .AsNoTracking()
            .Where(m => m.Id == request.MessageId && !m.IsDeleted)
            .Where(m => db.ConversationParticipants.Any(p => p.ConversationId == m.ConversationId && p.UserId == meId))
            .FirstOrDefaultAsync(cancellationToken);

        if (message is null || !message.HasAttachment
            || !await teams.IsCurrentMemberAsync(message.ConversationId, meId, cancellationToken))
        {
            return Result.Failure<ChatAttachmentDownload>(Error.NotFound("Attachment was not found."));
        }

        var stream = await fileStorage.OpenReadAsync(message.AttachmentStorageKey!, cancellationToken);
        return stream is null
            ? Result.Failure<ChatAttachmentDownload>(Error.NotFound("The attachment's content is missing from storage."))
            : Result.Success(new ChatAttachmentDownload(
                message.AttachmentFileName!,
                message.AttachmentContentType ?? "application/octet-stream",
                stream));
    }
}
