using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// With <see cref="OrgStructureOptions.LimitChatToHierarchy"/> on, a user chats only with: the
/// people at their level (the same direct manager), everyone below them (any depth), their
/// manager and their manager's manager. A user with no place in the structure is not limited.
/// Worked out once per request (it is scoped) for each user asked about.
/// </summary>
public sealed class HierarchyChatScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    IOptions<OrgStructureOptions> options,
    IOrgGraphProvider? graphs = null) : IChatPartnerScope
{
    private (HierarchyResolver Resolver, IReadOnlySet<Guid> Placed)? _resolver;
    private Guid _tenant;
    private readonly Dictionary<Guid, IReadOnlySet<Guid>?> _cache = [];

    public async Task<IReadOnlySet<Guid>?> GetPartnersAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (!options.Value.LimitChatToHierarchy)
        {
            return null;
        }

        if (_resolver is null || _tenant != tenantId)
        {
            _resolver = await BuildAsync(tenantId, cancellationToken);
            _tenant = tenantId;
            _cache.Clear();
        }

        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var (resolver, placed) = _resolver!.Value;
        var partners = PartnersOf(resolver, placed, userId);
        _cache[userId] = partners;
        return partners;
    }

    /// <summary>The people the user may chat with, or null when they have no place.</summary>
    public static IReadOnlySet<Guid>? PartnersOf(HierarchyResolver resolver, IReadOnlySet<Guid> placed, Guid userId)
    {
        if (!placed.Contains(userId))
        {
            return null;
        }

        var partners = new HashSet<Guid>(resolver.AllSubordinatesOf(userId).Select(s => s.UserId));
        var chain = resolver.ManagementChainOf(userId);
        partners.UnionWith(chain.Take(2));
        if (chain.Count > 0)
        {
            partners.UnionWith(resolver.DirectSubordinatesOf(chain[0]));
        }

        partners.Remove(userId);
        return partners;
    }

    private async Task<(HierarchyResolver Resolver, IReadOnlySet<Guid> Placed)?> BuildAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var placed = assignments.Select(a => a.UserId)
            .Concat(companies.Where(c => c.ManagerUserId is not null).Select(c => c.ManagerUserId!.Value))
            .ToHashSet();
        var seats = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seats, holdings);
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        return (resolver, placed);
    }
}
