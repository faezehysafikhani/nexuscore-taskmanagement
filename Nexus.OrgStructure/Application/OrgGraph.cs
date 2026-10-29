using Nexus.Organization.Application;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// The one picture of how units sit under units, built once from the data and read by everything
/// that needs the structure (managers, who may be given tasks, stand-ins, work teams, the chart).
/// A unit whose parent is null - or whose parent is not a unit (the company itself) - is at the top
/// of its company, under the company's implicit root: the root has no row and no manager, so a
/// structure where every unit is at the top is exactly the flat structure of before. Every walk
/// remembers where it has been, so data that loops can never hang a request.
/// </summary>
public sealed class OrgGraph
{
    private readonly IReadOnlyDictionary<Guid, UnitNode> _nodes;
    private readonly IReadOnlyDictionary<Guid, Guid> _companyOfLinkedUnit;
    private Dictionary<Guid, List<Guid>>? _children;

    public OrgGraph(IEnumerable<UnitNode> nodes, IReadOnlyDictionary<Guid, Guid> companyOfLinkedUnit)
    {
        _nodes = nodes.ToDictionary(node => node.Id);
        _companyOfLinkedUnit = companyOfLinkedUnit;
    }

    public IReadOnlyCollection<UnitNode> Nodes => _nodes.Values.ToList();

    /// <summary>The units linked to a company (the top unit of each company's branches) and their companies.</summary>
    public IReadOnlyDictionary<Guid, Guid> LinkedUnits => _companyOfLinkedUnit;

    /// <summary>
    /// The graph of the given units. A unit whose node type is not a domain boundary is not the
    /// boundary of «the same deputy»; a unit with no node type (a plain unit) is.
    /// </summary>
    public static OrgGraph Build(
        IEnumerable<Nexus.Organization.Domain.OrganizationUnit> units,
        IEnumerable<Domain.NodeType> nodeTypes,
        IEnumerable<Domain.CompanyUnit> links,
        IEnumerable<Domain.UnitPosition>? seats = null,
        IEnumerable<Domain.UserPosition>? holdings = null)
    {
        var boundaryOf = nodeTypes.ToDictionary(type => type.Id, type => type.IsDomainBoundary);
        var managerPostHolder = ManagerPostHolders(seats, holdings);
        return new OrgGraph(
            units.Select(unit => new UnitNode(
                unit.Id,
                unit.ParentUnitId,
                // The manager set on the unit; only when there is none, whoever holds its manager's post.
                unit.ManagerUserId ?? (managerPostHolder.TryGetValue(unit.Id, out var holder) ? holder : null),
                unit.NodeTypeId is { } typeId && boundaryOf.TryGetValue(typeId, out var boundary) ? boundary : true)),
            links.ToDictionary(link => link.UnitId, link => link.CompanyId));
    }

    /// <summary>Whether a unit exists on a day: from its ValidFrom (none: always before) to its ValidTo (none: always after).</summary>
    public static bool IsValidAt(Nexus.Organization.Domain.OrganizationUnit unit, DateTimeOffset date) =>
        (unit.ValidFrom is not { } from || from <= date) && (unit.ValidTo is not { } to || date < to);

    /// <summary>
    /// The graph as it is on a day: only the units that exist then (a unit under one that does not is
    /// left out too). Units with no dates exist always, so for today's data it is the same graph.
    /// </summary>
    public static OrgGraph AsOf(
        DateTimeOffset date,
        IEnumerable<Nexus.Organization.Domain.OrganizationUnit> units,
        IEnumerable<Domain.NodeType> nodeTypes,
        IEnumerable<Domain.CompanyUnit> links,
        IEnumerable<Domain.UnitPosition>? seats = null,
        IEnumerable<Domain.UserPosition>? holdings = null)
    {
        var all = units.ToDictionary(unit => unit.Id);
        bool Exists(Nexus.Organization.Domain.OrganizationUnit unit)
        {
            var seen = new HashSet<Guid>();
            for (var current = unit; current is not null && seen.Add(current.Id); current = current.ParentUnitId is { } p ? all.GetValueOrDefault(p) : null)
            {
                if (!IsValidAt(current, date))
                {
                    return false;
                }
            }

            return true;
        }

        var kept = all.Values.Where(Exists).ToList();
        var ids = kept.Select(unit => unit.Id).ToHashSet();
        return Build(kept, nodeTypes, links.Where(link => ids.Contains(link.UnitId)), seats?.Where(seat => seat.UnitId is not { } u || ids.Contains(u)), holdings);
    }

    /// <summary>Per unit, the holder of its manager's post (the first held one in the unit's order).</summary>
    private static Dictionary<Guid, Guid> ManagerPostHolders(IEnumerable<Domain.UnitPosition>? seats, IEnumerable<Domain.UserPosition>? holdings)
    {
        var result = new Dictionary<Guid, Guid>();
        if (seats is null || holdings is null)
        {
            return result;
        }

        var holderOf = holdings.GroupBy(h => h.UnitPositionId).ToDictionary(group => group.Key, group => group.First().UserId);
        foreach (var seat in seats.Where(seat => seat.IsManagerPost && seat.UnitId is not null).OrderBy(seat => seat.SortOrder))
        {
            if (holderOf.TryGetValue(seat.Id, out var holder))
            {
                result.TryAdd(seat.UnitId!.Value, holder);
            }
        }

        return result;
    }

    public UnitNode? Node(Guid unitId) => _nodes.GetValueOrDefault(unitId);

    /// <summary>The unit above this one; null when it is at the top (no parent, or the parent is not a unit).</summary>
    public Guid? ParentOf(Guid unitId) =>
        _nodes.TryGetValue(unitId, out var node) && node.ParentId is { } parent && _nodes.ContainsKey(parent) ? parent : null;

    public IReadOnlyList<Guid> ChildrenOf(Guid unitId) =>
        (_children ??= _nodes.Values.Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToList()))
        .GetValueOrDefault(unitId) ?? [];

    /// <summary>Every unit at or below the given one (cycle-safe).</summary>
    public IReadOnlyList<Guid> SubtreeOf(Guid unitId)
    {
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
            ChildrenOf(id).ToList().ForEach(pending.Push);
        }

        return result;
    }

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

            current = _nodes.TryGetValue(id, out var node) ? node.ParentId : null;
        }

        return null;
    }

    /// <summary>The company a unit belongs to: the one linked to it or to the nearest unit above it.</summary>
    public Guid? CompanyOfUnit(Guid unitId) => LinkedAncestorOf(unitId) is { } linked ? _companyOfLinkedUnit[linked] : null;

    /// <summary>
    /// The nodes a unit is under through secondary links that count as manager (CountsAsManager):
    /// the targets of such links from the unit itself or from any unit above it. countingLinks maps a
    /// node to the nodes its counting links point at. None when no link counts (the default).
    /// </summary>
    public IReadOnlyList<Guid> ExtraManagerNodesOf(Guid unitId, ILookup<Guid, Guid> countingLinks)
    {
        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        Guid? current = unitId;
        while (current is { } id && visited.Add(id))
        {
            result.AddRange(countingLinks[id].Where(target => _nodes.ContainsKey(target) && !result.Contains(target)));
            current = ParentOf(id);
        }

        return result;
    }

    /// <summary>
    /// «The same deputy»: the nearest unit at or above this one that is a domain boundary (a unit of
    /// a node type with IsDomainBoundary, which a plain unit is). Two people are in the same deputy
    /// when their units have the same domain. With no nesting every unit is its own domain.
    /// </summary>
    public Guid DomainOf(Guid unitId)
    {
        var visited = new HashSet<Guid>();
        Guid? current = unitId;
        while (current is { } id && visited.Add(id) && _nodes.TryGetValue(id, out var node))
        {
            if (node.IsDomainBoundary)
            {
                return id;
            }

            current = node.ParentId;
        }

        return unitId;
    }
}

/// <summary>Builds the <see cref="OrgGraph"/> of an organization. Ask once per operation and read from the result.</summary>
public interface IOrgGraphProvider
{
    Task<OrgGraph> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed class OrgGraphProvider(IOrganizationUnitRepository units, IOrgStructureRepository repository) : IOrgGraphProvider
{
    public async Task<OrgGraph> GetAsync(Guid tenantId, CancellationToken cancellationToken) => OrgGraph.Build(
        await units.ListAsync(tenantId, cancellationToken),
        await repository.ListNodeTypesAsync(tenantId, cancellationToken),
        await repository.ListCompanyUnitsAsync(tenantId, cancellationToken),
        await repository.ListUnitPositionsAsync(tenantId, cancellationToken),
        await repository.ListUserPositionsAsync(tenantId, cancellationToken));
}
