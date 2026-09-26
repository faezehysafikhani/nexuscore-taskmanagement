using Chat.Application.Abstractions;
using Chat.Application.Direct;
using Chat.Domain.Entities;
using Chat.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexusCore.Application.Files;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Teams;

/// <summary>One message of a team's shared conversation, with its sender (times are UTC).</summary>
public sealed record TeamMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid TeamId,
    Guid SenderUserId,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Text,
    DateTimeOffset SentAtUtc,
    DateTimeOffset? EditedAtUtc,
    ChatAttachmentDto? Attachment);

public sealed record TeamUnreadDto(Guid TeamId, int Count);

public sealed record GetTeamMessagesQuery(Guid TeamId, int Page = 1, int PageSize = 500) : IRequest<Result<List<TeamMessageDto>>>;

public sealed record SendTeamMessageCommand(Guid TeamId, string? Text, ChatAttachmentUpload? Attachment) : IRequest<Result<TeamMessageDto>>;

/// <summary>Marks everything the other members sent in the team's conversation as read. Returns how many were newly marked.</summary>
public sealed record MarkTeamConversationReadCommand(Guid TeamId) : IRequest<Result<int>>;

public sealed record GetTeamUnreadCountsQuery : IRequest<Result<List<TeamUnreadDto>>>;

/// <summary>
/// A team's one shared conversation (a group chat, never a direct chat per member). Who takes
/// part is the team's membership itself, read live from the directory: the active members of
/// the active team, in the caller's organization. The conversation's participant rows are
/// brought in line with that on every use, and anything that goes by those rows alone (the
/// hub, the generic message endpoints, attachments) also asks <see cref="IsCurrentMemberAsync"/>
/// - so a member taken off the team loses the thread at once, history included, and a new
/// member sees the whole thread.
/// </summary>
public sealed class TeamConversationService(
    IChatDbContext db,
    ICurrentUserContext currentUser,
    IUserDirectory userDirectory)
{
    /// <summary>The caller and the team's active members (caller included), or "not found" for a team they are not in.</summary>
    public async Task<Result<(UserContact Me, IReadOnlyList<UserContact> Members)>> ResolveAsync(Guid teamId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } meId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<(UserContact, IReadOnlyList<UserContact>)>(Error.Unauthorized());
        }

        // Active teams the caller belongs to (as member or owner); anything else is "not found".
        var myTeams = await userDirectory.GetGroupIdsOfUserAsync(meId, cancellationToken);
        if (!myTeams.Contains(teamId))
        {
            return Result.Failure<(UserContact, IReadOnlyList<UserContact>)>(Error.NotFound("Team was not found."));
        }

        var memberIds = (await userDirectory.GetGroupMemberIdsAsync(teamId, cancellationToken)).Append(meId).Distinct().ToList();
        var members = (await userDirectory.GetUsersAsync(memberIds, cancellationToken))
            .Where(user => user.TenantId == tenantId && user.IsActive)
            .ToList();
        var me = members.FirstOrDefault(user => user.Id == meId);
        return me is null
            ? Result.Failure<(UserContact, IReadOnlyList<UserContact>)>(Error.NotFound("Team was not found."))
            : Result.Success<(UserContact, IReadOnlyList<UserContact>)>((me, members));
    }

    public Task<Conversation?> FindAsync(Guid teamId, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.TeamId == teamId && c.TenantId == currentUser.TenantId, cancellationToken);

    /// <summary>The team's conversation (created on first use) with its participants matching the members.</summary>
    public async Task<Conversation> FindOrCreateAsync(Guid teamId, IReadOnlyList<UserContact> members, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(teamId, cancellationToken);
        if (conversation is null)
        {
            conversation = Conversation.CreateForTeam(Guid.NewGuid(), currentUser.TenantId!.Value, teamId, currentUser.UserId);
            db.Conversations.Add(conversation);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Someone else created it at the same moment: use theirs.
                db.ChangeTracker.Clear();
                conversation = await FindAsync(teamId, cancellationToken)
                               ?? throw new InvalidOperationException("The team conversation could not be created.");
            }
        }

        await SyncParticipantsAsync(conversation, members, cancellationToken);
        return conversation;
    }

    /// <summary>Adds members who joined and removes those who left (or were disabled).</summary>
    public async Task SyncParticipantsAsync(Conversation conversation, IReadOnlyList<UserContact> members, CancellationToken cancellationToken)
    {
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var participants = await db.ConversationParticipants
            .Where(p => p.ConversationId == conversation.Id)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var gone in participants.Where(p => p.UserId is not { } id || !memberIds.Contains(id)))
        {
            db.ConversationParticipants.Remove(gone);
            changed = true;
        }

        foreach (var id in memberIds.Where(id => participants.All(p => p.UserId != id)))
        {
            db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = conversation.Id, UserId = id, JoinedAt = DateTime.UtcNow });
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request synced the same rows at the same moment; theirs is as good.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// For a team's conversation: whether the user is still an active member of the team. Always
    /// true for other conversations (their participant rows decide).
    /// </summary>
    public async Task<bool> IsCurrentMemberAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var teamId = await db.Conversations.Where(c => c.Id == conversationId).Select(c => c.TeamId).FirstOrDefaultAsync(cancellationToken);
        if (teamId is not { } team)
        {
            return true;
        }

        if (!(await userDirectory.GetGroupIdsOfUserAsync(userId, cancellationToken)).Contains(team))
        {
            return false;
        }

        var user = (await userDirectory.GetUsersAsync([userId], cancellationToken)).FirstOrDefault();
        return user is { IsActive: true } && user.TenantId == currentUser.TenantId;
    }

    public static TeamMessageDto ToDto(Message message, Guid teamId, UserContact? sender) =>
        new(
            message.Id,
            message.ConversationId,
            teamId,
            message.SenderUserId ?? Guid.Empty,
            sender?.DisplayName ?? "کاربر",
            sender?.AvatarUrl,
            message.Text,
            DirectConversationService.Utc(message.SentAt),
            message.EditedAt is { } edited ? DirectConversationService.Utc(edited) : null,
            message.HasAttachment
                ? new ChatAttachmentDto(message.AttachmentFileName!, message.AttachmentContentType ?? "application/octet-stream", message.AttachmentSizeBytes ?? 0)
                : null);
}

public sealed class GetTeamMessagesQueryHandler(
    IChatDbContext db,
    TeamConversationService teams,
    IUserDirectory userDirectory)
    : IRequestHandler<GetTeamMessagesQuery, Result<List<TeamMessageDto>>>
{
    public async Task<Result<List<TeamMessageDto>>> Handle(GetTeamMessagesQuery request, CancellationToken cancellationToken)
    {
        var team = await teams.ResolveAsync(request.TeamId, cancellationToken);
        if (team.IsFailure)
        {
            return Result.Failure<List<TeamMessageDto>>(team.Error);
        }

        var conversation = await teams.FindAsync(request.TeamId, cancellationToken);
        if (conversation is null)
        {
            return Result.Success(new List<TeamMessageDto>());
        }

        await teams.SyncParticipantsAsync(conversation, team.Value.Members, cancellationToken);

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

        // Senders by name, former members included (their messages stay in the thread).
        var senderIds = messages.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct().ToList();
        var senders = (await userDirectory.GetUsersAsync(senderIds, cancellationToken)).ToDictionary(u => u.Id);
        return Result.Success(messages
            .Select(m => TeamConversationService.ToDto(m, request.TeamId, m.SenderUserId is { } s && senders.TryGetValue(s, out var u) ? u : null))
            .ToList());
    }
}

public sealed class SendTeamMessageCommandHandler(
    IChatDbContext db,
    TeamConversationService teams,
    IFileStorage fileStorage,
    IOptions<ChatOptions> options)
    : IRequestHandler<SendTeamMessageCommand, Result<TeamMessageDto>>
{
    public async Task<Result<TeamMessageDto>> Handle(SendTeamMessageCommand request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && request.Attachment is null)
        {
            return Result.Failure<TeamMessageDto>(Error.Validation("A message needs text or an attachment."));
        }

        if (text.Length > DirectConversationService.MaxTextLength)
        {
            return Result.Failure<TeamMessageDto>(Error.Validation($"A message can be at most {DirectConversationService.MaxTextLength} characters."));
        }

        if (request.Attachment is { } upload && (upload.Length <= 0 || upload.Length > options.Value.MaxAttachmentBytes))
        {
            return Result.Failure<TeamMessageDto>(Error.Validation(upload.Length <= 0
                ? "The attached file is empty."
                : $"The attached file is too large. The limit is {options.Value.MaxAttachmentBytes / (1024 * 1024)} MB."));
        }

        var team = await teams.ResolveAsync(request.TeamId, cancellationToken);
        if (team.IsFailure)
        {
            return Result.Failure<TeamMessageDto>(team.Error);
        }

        var (me, members) = team.Value;
        var conversation = await teams.FindOrCreateAsync(request.TeamId, members, cancellationToken);
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
        return Result.Success(TeamConversationService.ToDto(message, request.TeamId, me));
    }
}

public sealed class MarkTeamConversationReadCommandHandler(
    IChatDbContext db,
    TeamConversationService teams)
    : IRequestHandler<MarkTeamConversationReadCommand, Result<int>>
{
    public async Task<Result<int>> Handle(MarkTeamConversationReadCommand request, CancellationToken cancellationToken)
    {
        var team = await teams.ResolveAsync(request.TeamId, cancellationToken);
        if (team.IsFailure)
        {
            return Result.Failure<int>(team.Error);
        }

        var meId = team.Value.Me.Id;
        var conversation = await teams.FindAsync(request.TeamId, cancellationToken);
        if (conversation is null)
        {
            return Result.Success(0);
        }

        var unreadIds = await db.Messages
            .Where(m => m.ConversationId == conversation.Id && !m.IsDeleted && m.SenderUserId != meId)
            .Where(m => !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == meId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        if (unreadIds.Count == 0)
        {
            return Result.Success(0);
        }

        db.MessageReads.AddRange(unreadIds.Select(id => new MessageRead(Guid.NewGuid(), id, meId)));
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

public sealed class GetTeamUnreadCountsQueryHandler(
    IChatDbContext db,
    ICurrentUserContext currentUser,
    IUserDirectory userDirectory)
    : IRequestHandler<GetTeamUnreadCountsQuery, Result<List<TeamUnreadDto>>>
{
    public async Task<Result<List<TeamUnreadDto>>> Handle(GetTeamUnreadCountsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } meId)
        {
            return Result.Failure<List<TeamUnreadDto>>(Error.Unauthorized());
        }

        // Only the teams the caller is in now - a former member's old thread is not counted.
        var myTeams = await userDirectory.GetGroupIdsOfUserAsync(meId, cancellationToken);
        if (myTeams.Count == 0)
        {
            return Result.Success(new List<TeamUnreadDto>());
        }

        var counts = await db.Messages
            .AsNoTracking()
            .Where(m => !m.IsDeleted && m.SenderUserId != null && m.SenderUserId != meId)
            .Where(m => !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == meId))
            .Join(
                db.Conversations.Where(c => c.TeamId != null && c.TenantId == currentUser.TenantId && myTeams.Contains(c.TeamId!.Value)),
                m => m.ConversationId,
                c => c.Id,
                (m, c) => c.TeamId!.Value)
            .GroupBy(teamId => teamId)
            .Select(g => new TeamUnreadDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return Result.Success(counts);
    }
}
