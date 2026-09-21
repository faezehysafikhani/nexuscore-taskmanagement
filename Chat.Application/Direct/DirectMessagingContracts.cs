using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Direct;

/// <summary>Chat settings (Chat section in configuration).</summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Largest attachment accepted on a message. Matches the chat screen's 5 MB limit.</summary>
    public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
}

public sealed record ChatAttachmentDto(string FileName, string ContentType, long SizeBytes);

/// <summary>
/// One message of a conversation between two users. Times are UTC. IsRead tells whether the
/// receiver has read it - for your own messages that is the other person.
/// </summary>
public sealed record DirectMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    Guid ReceiverUserId,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Text,
    DateTimeOffset SentAtUtc,
    DateTimeOffset? EditedAtUtc,
    bool IsRead,
    ChatAttachmentDto? Attachment);

public sealed record UnreadBySenderDto(Guid SenderUserId, int Count);

/// <summary>An uploaded file on its way to storage. The stream is owned by the caller.</summary>
public sealed record ChatAttachmentUpload(string FileName, string ContentType, long Length, Stream Content);

public sealed record ChatAttachmentDownload(string FileName, string ContentType, Stream Content);

public sealed record GetDirectMessagesQuery(Guid OtherUserId, int Page = 1, int PageSize = 500)
    : IRequest<Result<List<DirectMessageDto>>>;

public sealed record SendDirectMessageCommand(Guid OtherUserId, string? Text, ChatAttachmentUpload? Attachment)
    : IRequest<Result<DirectMessageDto>>;

/// <summary>Marks everything the other user sent you as read. Returns how many were newly marked.</summary>
public sealed record MarkDirectConversationReadCommand(Guid OtherUserId) : IRequest<Result<int>>;

public sealed record GetUnreadCountsBySenderQuery : IRequest<Result<List<UnreadBySenderDto>>>;

public sealed record GetMessageAttachmentQuery(Guid MessageId) : IRequest<Result<ChatAttachmentDownload>>;
