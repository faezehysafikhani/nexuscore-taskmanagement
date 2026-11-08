namespace Nexus.ProjectManagement.Core.Application;

/// <summary>
/// What a user may see of other people's work, beyond what they own, manage or are responsible for. An
/// optional extension point: Portfolio asks every registered provider and shows the union, so installing
/// an integration (team membership, organisation chart, approvals) widens visibility without Portfolio
/// knowing any of them. With no provider registered, visibility is exactly owner/manager/responsible.
/// Granting never exceeds the caller's tenant; a provider only ever returns ids of that tenant.
/// </summary>
public interface IVisibilityProvider
{
    Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}

/// <param name="ProjectIds">Projects the user may see, whoever owns them.</param>
/// <param name="ActionIds">Actions the user may see.</param>
/// <param name="OrganizationUnitIds">Organisation units whose projects and actions the user may see.</param>
public sealed record VisibilityGrant(
    IReadOnlySet<Guid> ProjectIds,
    IReadOnlySet<Guid> ActionIds,
    IReadOnlySet<Guid> OrganizationUnitIds)
{
    public static VisibilityGrant None { get; } = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>The combination of several grants: everything any of them allows.</summary>
    public static VisibilityGrant Union(IEnumerable<VisibilityGrant> grants)
    {
        var projects = new HashSet<Guid>();
        var actions = new HashSet<Guid>();
        var units = new HashSet<Guid>();
        foreach (var grant in grants)
        {
            projects.UnionWith(grant.ProjectIds);
            actions.UnionWith(grant.ActionIds);
            units.UnionWith(grant.OrganizationUnitIds);
        }

        return new VisibilityGrant(projects, actions, units);
    }
}
