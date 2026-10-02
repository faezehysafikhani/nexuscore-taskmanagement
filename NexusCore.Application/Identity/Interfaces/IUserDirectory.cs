namespace NexusCore.Application.Identity.Interfaces;

/// <summary>What another module may know about a user: who they are and how to reach them.</summary>
public sealed record UserContact(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string? Email,
    string? Username,
    string? AvatarUrl,
    string? PhoneNumber,
    bool NotifySms,
    bool IsActive);

/// <summary>
/// Read-only view of the shared identity data for modules that must not own a copy of it
/// (chat, notifications). Keeps every module on the one User table instead of duplicating it.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Every active user of the tenant, for picking a chat partner - not an administrative listing.</summary>
    Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>The active groups (and work teams) the user is a member or the owner of.</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken);
}
