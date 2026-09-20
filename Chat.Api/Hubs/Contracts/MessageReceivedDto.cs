namespace Chat.Api.Hubs.Contracts;

public sealed record MessageReceivedDto(
    Guid Id,
    Guid ConversationId,
    Guid? SenderUserId,
    string Text,
    DateTime SentAt);
