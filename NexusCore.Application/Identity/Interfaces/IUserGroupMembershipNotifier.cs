namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Told who was just added to a group or work team, so they can be let know. The Core has no
/// notification channel of its own: the default does nothing, and a host that composes the
/// task-notifications integration gets an in-app notification for each new member.
/// Called after the membership is saved; an implementation must not throw.
/// </summary>
public interface IUserGroupMembershipNotifier
{
    Task MembersAddedAsync(UserGroupMembersAdded change, CancellationToken cancellationToken);
}

/// <param name="AddedByName">Display name of the person who added them, when known.</param>
/// <param name="UserIds">The new members - never the person who added them.</param>
public sealed record UserGroupMembersAdded(
    Guid TenantId,
    Guid GroupId,
    string GroupName,
    bool IsPersonalTeam,
    string? AddedByName,
    IReadOnlyList<Guid> UserIds);

public sealed class NullUserGroupMembershipNotifier : IUserGroupMembershipNotifier
{
    public Task MembersAddedAsync(UserGroupMembersAdded change, CancellationToken cancellationToken) => Task.CompletedTask;
}
