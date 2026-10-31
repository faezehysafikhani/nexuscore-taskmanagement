using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// With <see cref="OrgStructureOptions.LimitChatToHierarchy"/> on, a user chats only with: the
/// people at their level, everyone below them (any depth), their managers and their managers'
/// managers. Worked out from every post each person holds now (not only their main one): the
/// manager of a post is the holder of the nearest post above it that someone holds, else - a
/// vacant chain above, or no post at all - the manager from their place in the chart. The people
/// at one's level share a manager and a unit. A user with no place is not limited.
/// Worked out once per request (it is scoped) for each user asked about.
/// </summary>
public sealed class HierarchyChatScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    IOptions<OrgStructureOptions> options,
    IOrgGraphProvider? graphs = null) : IChatPartnerScope
{
    private Model? _model;
    private Guid _tenant;
    private readonly Dictionary<Guid, IReadOnlySet<Guid>?> _cache = [];

    public async Task<IReadOnlySet<Guid>?> GetPartnersAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (!options.Value.LimitChatToHierarchy)
        {
            return null;
        }

        if (_model is null || _tenant != tenantId)
        {
            _model = await BuildAsync(tenantId, cancellationToken);
            _tenant = tenantId;
            _cache.Clear();
        }

        if (!_cache.TryGetValue(userId, out var partners))
        {
            partners = _model.PartnersOf(userId);
            _cache[userId] = partners;
        }

        return partners;
    }

    /// <summary>Each placed person's managers and units, and who may chat with whom.</summary>
    public sealed class Model(IReadOnlyDictionary<Guid, HashSet<Guid>> managersOf, IReadOnlyDictionary<Guid, HashSet<Guid?>> unitsOf, IReadOnlySet<Guid> withoutPost)
    {
        public IReadOnlySet<Guid>? PartnersOf(Guid userId)
        {
            if (!managersOf.TryGetValue(userId, out var mine))
            {
                return null;
            }

            var partners = new HashSet<Guid>(mine);
            foreach (var manager in mine)
            {
                partners.UnionWith(managersOf.GetValueOrDefault(manager) ?? []);
            }

            // Below: everyone whose managers lead back to the user, at any depth.
            var reportsOf = managersOf.Where(pair => !withoutPost.Contains(pair.Key))
                .SelectMany(pair => pair.Value.Select(manager => (manager, report: pair.Key)))
                .ToLookup(x => x.manager, x => x.report);
            var pending = new Queue<Guid>([userId]);
            var below = new HashSet<Guid> { userId };
            while (pending.Count > 0)
            {
                foreach (var report in reportsOf[pending.Dequeue()].Where(below.Add))
                {
                    pending.Enqueue(report);
                }
            }

            partners.UnionWith(below);

            // The same level: a manager in common, and a unit in common.
            var myUnits = unitsOf.GetValueOrDefault(userId) ?? [];
            foreach (var manager in mine)
            {
                partners.UnionWith(reportsOf[manager].Where(peer => (unitsOf.GetValueOrDefault(peer) ?? []).Overlaps(myUnits)));
            }

            partners.Remove(userId);
            return partners;
        }
    }

    private async Task<Model> BuildAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var seats = (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).ToDictionary(seat => seat.Id);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seats.Values.ToList(), holdings);
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));

        var holderOf = holdings.GroupBy(h => h.UnitPositionId).ToDictionary(g => g.Key, g => g.First().UserId);
        var postsOf = holdings.GroupBy(h => h.UserId).ToDictionary(g => g.Key, g => g.Select(h => h.UnitPositionId).ToList());
        var placed = assignments.Select(a => a.UserId)
            .Concat(companies.Where(c => c.ManagerUserId is not null).Select(c => c.ManagerUserId!.Value))
            .Concat(postsOf.Keys)
            .ToHashSet();

        var heads = companies.ToDictionary(c => c.Id, c => c.ManagerUserId);
        Guid? UnitManagerOf(Domain.UnitPosition seat, Guid person)
        {
            var visited = new HashSet<Guid>();
            for (var current = seat.UnitId; current is { } unitId && visited.Add(unitId) && graph.Node(unitId) is { } unit; current = unit.ParentId)
            {
                if (unit.ManagerUserId is { } manager && manager != person)
                {
                    return manager;
                }

                if (resolver.LinkedAncestorOf(unitId) == unitId)
                {
                    break;
                }
            }

            return heads.GetValueOrDefault(seat.CompanyId) is { } head && head != person ? head : null;
        }

        var managersOf = new Dictionary<Guid, HashSet<Guid>>();
        var unitsOf = new Dictionary<Guid, HashSet<Guid?>>();
        foreach (var person in placed)
        {
            var managers = new HashSet<Guid>();
            var personUnits = new HashSet<Guid?>();
            foreach (var postId in postsOf.GetValueOrDefault(person) ?? [])
            {
                if (!seats.TryGetValue(postId, out var seat))
                {
                    continue;
                }

                personUnits.Add(seat.UnitId);
                Guid? above = null;
                var visited = new HashSet<Guid> { seat.Id };
                for (var parent = seat.ParentPostId; parent is { } parentId && visited.Add(parentId) && seats.TryGetValue(parentId, out var up); parent = up.ParentPostId)
                {
                    if (holderOf.TryGetValue(parentId, out var holder) && holder != person)
                    {
                        above = holder;
                        break;
                    }
                }

                // Only vacant posts above (or none): the manager of the post's own unit, or of the
                // nearest unit above it in the company, else the company's head.
                above ??= UnitManagerOf(seat, person);
                if (above is { } found)
                {
                    managers.Add(found);
                }
            }

            // No post: the manager from their place in the chart.
            if (!postsOf.ContainsKey(person))
            {
                if (resolver.ManagerOf(person)?.ManagerUserId is { } manager && manager != person)
                {
                    managers.Add(manager);
                }

                personUnits.Add(assignments.FirstOrDefault(a => a.UserId == person)?.UnitId);
            }

            managersOf[person] = managers;
            unitsOf[person] = personUnits;
        }

        // In a company where people hold posts, someone with no post now (e.g. replaced on their last
        // one) is nobody's colleague or subordinate any more; they still reach their own manager.
        var postCompanies = holdings.Select(h => seats.GetValueOrDefault(h.UnitPositionId)?.CompanyId).OfType<Guid>().ToHashSet();
        var withoutPost = assignments.Where(a => !postsOf.ContainsKey(a.UserId) && postCompanies.Contains(a.CompanyId)).Select(a => a.UserId).ToHashSet();
        return new Model(managersOf, unitsOf, withoutPost);
    }
}
