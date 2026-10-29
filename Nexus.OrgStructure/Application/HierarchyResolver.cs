namespace Nexus.OrgStructure.Application;

/// <summary>How a user's manager was determined.</summary>
public enum ManagerSource
{
    /// <summary>Set on the user's own assignment.</summary>
    Explicit = 0,

    /// <summary>The manager of the user's unit, or of the nearest unit above it in the same company.</summary>
    UnitManager = 1,

    /// <summary>The head of the user's company.</summary>
    CompanyManager = 2,

    /// <summary>The holder of the post above the user's own (the nearest one someone holds).</summary>
    PostSuperior = 3
}

/// <summary>IsDomainBoundary: whether the unit is the boundary of «the same deputy» (see <see cref="OrgGraph.DomainOf"/>); true for a plain unit.</summary>
public sealed record UnitNode(Guid Id, Guid? ParentId, Guid? ManagerUserId, bool IsDomainBoundary = true);

/// <summary>PostSuperiorUserId: who holds the post above the user's own (see <see cref="PostSuperiors"/>).</summary>
public sealed record AssignmentNode(Guid UserId, Guid CompanyId, Guid? UnitId, Guid? ManagerUserId, Guid? PostSuperiorUserId = null);

/// <summary>
/// Posts under posts (معاون above کارشناس, a head of office above its staff...): a placed person's
/// superior through their post is the holder of the nearest post above their main post that
/// someone else holds; a vacant post above is passed over. The main post: the one of their main
/// position in their own place (unit, or directly in the company), else the first they hold there.
/// </summary>
public static class PostSuperiors
{
    public sealed record Placement(Guid UserId, Guid CompanyId, Guid? UnitId, Guid? PositionId);

    public static IReadOnlyDictionary<Guid, Guid> Of(
        IEnumerable<Placement> placements,
        IEnumerable<Domain.UnitPosition> seats,
        IEnumerable<Domain.UserPosition> holdings,
        Func<Domain.UnitPosition, Guid?>? parentOf = null)
    {
        parentOf ??= seat => seat.ParentPostId;
        var byId = seats.ToDictionary(seat => seat.Id);
        var holdingList = holdings.ToList();
        var holderOf = holdingList.GroupBy(h => h.UnitPositionId).ToDictionary(group => group.Key, group => group.First().UserId);
        var heldBy = holdingList.ToLookup(h => h.UserId, h => h.UnitPositionId);
        var result = new Dictionary<Guid, Guid>();
        foreach (var placement in placements)
        {
            var own = heldBy[placement.UserId].Select(id => byId.GetValueOrDefault(id)).OfType<Domain.UnitPosition>()
                .Where(seat => seat.CompanyId == placement.CompanyId && seat.UnitId == placement.UnitId)
                .ToList();
            var main = own.FirstOrDefault(seat => seat.PositionId == placement.PositionId) ?? own.OrderBy(seat => seat.SortOrder).FirstOrDefault();
            if (main is null)
            {
                continue;
            }

            var visited = new HashSet<Guid> { main.Id };
            var parent = parentOf(main);
            while (parent is { } postId && visited.Add(postId) && byId.TryGetValue(postId, out var above))
            {
                if (holderOf.TryGetValue(postId, out var holder) && holder != placement.UserId)
                {
                    result[placement.UserId] = holder;
                    break;
                }

                parent = parentOf(above);
            }
        }

        return result;
    }
}

public sealed record ResolvedManager(Guid ManagerUserId, ManagerSource Source);

/// <summary>
/// The whole reporting structure of one tenant, computed in memory from the module's own rows
/// and Nexus.Organization's units. A user's manager is, in order: the one set on their
/// assignment; else the manager of their unit (or of the nearest unit above it, up to the unit
/// linked to the company) who is not the user themselves; else the company's head. Every walk
/// remembers where it has been, so data that loops (e.g. after units are moved in
/// Nexus.Organization) can never hang a request; writes through this module reject loops.
/// </summary>
public sealed class HierarchyResolver
{
    private readonly OrgGraph _graph;
    private readonly IReadOnlyDictionary<Guid, Guid> _companyOfLinkedUnit;
    private readonly IReadOnlyDictionary<Guid, Guid?> _companyManagers;
    private readonly IReadOnlyDictionary<Guid, AssignmentNode> _assignments;
    private Dictionary<Guid, ResolvedManager>? _managers;

    public HierarchyResolver(
        IEnumerable<UnitNode> units,
        IReadOnlyDictionary<Guid, Guid> companyOfLinkedUnit,
        IReadOnlyDictionary<Guid, Guid?> companyManagers,
        IEnumerable<AssignmentNode> assignments)
    {
        _graph = new OrgGraph(units, companyOfLinkedUnit);
        _companyOfLinkedUnit = companyOfLinkedUnit;
        _companyManagers = companyManagers;
        _assignments = assignments.ToDictionary(assignment => assignment.UserId);
    }

    public IReadOnlyDictionary<Guid, AssignmentNode> Assignments => _assignments;

    /// <summary>The graph of units this resolver walks (the one algorithm for parents, subtrees and domains).</summary>
    public OrgGraph Graph => _graph;

    /// <summary>The company a unit belongs to: the one linked to it or to the nearest unit above it.</summary>
    public Guid? CompanyOfUnit(Guid unitId) => _graph.CompanyOfUnit(unitId);

    /// <summary>The unit itself or the nearest unit above it that is linked to a company.</summary>
    public Guid? LinkedAncestorOf(Guid unitId) => _graph.LinkedAncestorOf(unitId);

    /// <summary>Every unit at or below the given one (cycle-safe).</summary>
    public IReadOnlyList<Guid> SubtreeOf(Guid unitId) => _graph.SubtreeOf(unitId);

    public ResolvedManager? ManagerOf(Guid userId) => Managers.TryGetValue(userId, out var manager) ? manager : null;

    /// <summary>The user's managers, nearest first, stopping before a loop would repeat anyone.</summary>
    public IReadOnlyList<Guid> ManagementChainOf(Guid userId)
    {
        var chain = new List<Guid>();
        var visited = new HashSet<Guid> { userId };
        var current = userId;
        while (ManagerOf(current) is { } manager && visited.Add(manager.ManagerUserId))
        {
            chain.Add(manager.ManagerUserId);
            current = manager.ManagerUserId;
        }

        return chain;
    }

    public IReadOnlyList<Guid> DirectSubordinatesOf(Guid userId) =>
        Managers.Where(pair => pair.Value.ManagerUserId == userId).Select(pair => pair.Key).ToList();

    /// <summary>Everyone below the user, at any depth, each once, with their distance (1 = direct).</summary>
    public IReadOnlyList<(Guid UserId, int Depth)> AllSubordinatesOf(Guid userId)
    {
        var byManager = Managers
            .GroupBy(pair => pair.Value.ManagerUserId)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToList());

        var result = new List<(Guid, int)>();
        var visited = new HashSet<Guid> { userId };
        var pending = new Queue<(Guid UserId, int Depth)>([(userId, 0)]);
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();
            if (!byManager.TryGetValue(current, out var reports))
            {
                continue;
            }

            foreach (var report in reports.Where(visited.Add))
            {
                result.Add((report, depth + 1));
                pending.Enqueue((report, depth + 1));
            }
        }

        return result;
    }

    /// <summary>A user whose management chain comes back to them, if there is one.</summary>
    public Guid? FindLoop()
    {
        foreach (var userId in Managers.Keys)
        {
            var visited = new HashSet<Guid> { userId };
            var current = userId;
            while (ManagerOf(current) is { } manager)
            {
                if (manager.ManagerUserId == userId)
                {
                    return userId;
                }

                if (!visited.Add(manager.ManagerUserId))
                {
                    break;
                }

                current = manager.ManagerUserId;
            }
        }

        return null;
    }

    private Dictionary<Guid, ResolvedManager> Managers => _managers ??= _assignments.Values
        .Select(assignment => (assignment.UserId, Manager: Resolve(assignment)))
        .Where(pair => pair.Manager is not null)
        .ToDictionary(pair => pair.UserId, pair => pair.Manager!);

    private ResolvedManager? Resolve(AssignmentNode assignment)
    {
        if (assignment.ManagerUserId is { } explicitManager && explicitManager != assignment.UserId)
        {
            return new ResolvedManager(explicitManager, ManagerSource.Explicit);
        }

        if (assignment.PostSuperiorUserId is { } superior && superior != assignment.UserId)
        {
            return new ResolvedManager(superior, ManagerSource.PostSuperior);
        }

        var visited = new HashSet<Guid>();
        var current = assignment.UnitId;
        while (current is { } unitId && visited.Add(unitId) && _graph.Node(unitId) is { } unit)
        {
            if (unit.ManagerUserId is { } unitManager && unitManager != assignment.UserId)
            {
                return new ResolvedManager(unitManager, ManagerSource.UnitManager);
            }

            // The unit linked to the company is the top of the company's branch: above it, the
            // company's own head takes over (units above may belong to someone else entirely).
            if (_companyOfLinkedUnit.ContainsKey(unitId))
            {
                break;
            }

            current = unit.ParentId;
        }

        return _companyManagers.TryGetValue(assignment.CompanyId, out var head) && head is { } companyHead && companyHead != assignment.UserId
            ? new ResolvedManager(companyHead, ManagerSource.CompanyManager)
            : null;
    }
}
