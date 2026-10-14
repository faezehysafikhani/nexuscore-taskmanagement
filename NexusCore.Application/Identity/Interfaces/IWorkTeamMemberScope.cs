namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whom the builder of a work team may add to it. The Core does not limit it: the default lets
/// anyone in the tenant be added. A product's organization structure can limit it (Nexus.OrgStructure
/// keeps a builder to the people of their own deputy unless they hold its permission for other
/// deputies). Only people being added are checked - members a team already has always stay.
/// </summary>
public interface IWorkTeamMemberScope
{
    /// <summary>Who <paramref name="ownerUserId"/> may add to their teams, or null when anyone may be added.</summary>
    Task<WorkTeamMemberLimit?> GetLimitAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken);
}

/// <param name="AllowedUserIds">The people who may be added.</param>
/// <param name="ScopeName">What they share, to tell the user (e.g. the deputy's name).</param>
public sealed record WorkTeamMemberLimit(IReadOnlySet<Guid> AllowedUserIds, string ScopeName);

public sealed class UnlimitedWorkTeamMemberScope : IWorkTeamMemberScope
{
    public Task<WorkTeamMemberLimit?> GetLimitAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<WorkTeamMemberLimit?>(null);
}
