namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whom a user may reach at all: see in user lists, message, add to a team, give a task to or
/// give access to one. The Core does not limit it: the default reaches everyone in the tenant.
/// A product's organization structure can limit it - Nexus.OrgStructure, when
/// OrgStructure:LimitToOwnCompany is on, keeps each user to the people of their own company.
/// Other rules (the level limit on tasks, a deputy's work teams) apply on top of this one.
/// </summary>
public interface IUserReachScope
{
    /// <summary>Whom <paramref name="userId"/> (the signed-in user) may reach, or null when everyone.</summary>
    Task<UserReach?> GetReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}

/// <param name="UserIds">The people who may be reached - always the user themselves too.</param>
/// <param name="PlacedElsewhere">
/// People who belong to other companies only. Everyone else outside <paramref name="UserIds"/> has
/// no place yet: a user list still shows them to whoever may place them, but they cannot be reached.
/// </param>
public sealed record UserReach(IReadOnlySet<Guid> UserIds, IReadOnlySet<Guid> PlacedElsewhere)
{
    public bool Contains(Guid userId) => UserIds.Contains(userId);
}

public sealed class UnlimitedUserReachScope : IUserReachScope
{
    public Task<UserReach?> GetReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<UserReach?>(null);
}
