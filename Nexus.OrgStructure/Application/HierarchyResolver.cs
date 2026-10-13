namespace Nexus.OrgStructure.Application;

/// <summary>How a user's manager was determined.</summary>
public enum ManagerSource
{
    /// <summary>Set on the user's own assignment.</summary>
    Explicit = 0,

    /// <summary>The manager of the user's unit, or of the nearest unit above it in the same company.</summary>
    UnitManager = 1,

    /// <summary>The head of the user's company.</summary>
    CompanyManager = 2
}

public sealed record UnitNode(Guid Id, Guid? ParentId, Guid? ManagerUserId);

public sealed record AssignmentNode(Guid UserId, Guid CompanyId, Guid? UnitId, Guid? ManagerUserId);

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
    private readonly IReadOnlyDictionary<Guid, UnitNode> _units;
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
        _units = units.ToDictionary(unit => unit.Id);
        _companyOfLinkedUnit = companyOfLinkedUnit;
        _companyManagers = companyManagers;
        _assignments = assignments.ToDictionary(assignment => assignment.UserId);
    }

    public IReadOnlyDictionary<Guid, AssignmentNode> Assignments => _assignments;

    /// <summary>The company a unit belongs to: the one linked to it or to the nearest unit above it.</summary>
    public Guid? CompanyOfUnit(Guid unitId) => LinkedAncestorOf(unitId) is { } linked ? _companyOfLinkedUnit[linked] : null;

    /// <summary>The unit itself or the nearest unit above it that is linked to a company.</summary>
    public Guid? LinkedAncestorOf(Guid unitId)
    {
        var visited = new HashSet<Guid>();
        Guid? current = unitId;
        while (current is { } id && visited.Add(id))
        {
            if (_companyOfLinkedUnit.ContainsKey(id))
            {
                return id;
            }

            current = _units.TryGetValue(id, out var unit) ? unit.ParentId : null;
        }

        return null;
    }

    /// <summary>Every unit at or below the given one (cycle-safe).</summary>
    public IReadOnlyList<Guid> SubtreeOf(Guid unitId)
    {
        var children = _units.Values
            .Where(unit => unit.ParentId is not null)
            .GroupBy(unit => unit.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(unit => unit.Id).ToList());

        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>([unitId]);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!visited.Add(id))
            {
                continue;
            }

            result.Add(id);
            if (children.TryGetValue(id, out var below))
            {
                below.ForEach(pending.Push);
            }
        }

        return result;
    }

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

        var visited = new HashSet<Guid>();
        var current = assignment.UnitId;
        while (current is { } unitId && visited.Add(unitId) && _units.TryGetValue(unitId, out var unit))
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
