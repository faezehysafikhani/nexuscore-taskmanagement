namespace NexusCore.Application.Identity.Interfaces;

/// <summary>What another module may know about a user: who they are and how to reach them.</summary>
public sealed record UserContact(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Email,
    string? Username,
    string? AvatarUrl,
    string? PhoneNumber,
    string? TelegramChatId,
    bool NotifySms,
    bool NotifyTelegram,
    bool IsActive);

/// <summary>
/// Read-only view of the shared identity data for modules that must not own a copy of it
/// (chat, notifications). Keeps every module on the one User table instead of duplicating it.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken);
}
