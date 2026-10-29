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
    public async Task<OrgGraph> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var unitList = await units.ListAsync(tenantId, cancellationToken);
        var boundaryOf = (await repository.ListNodeTypesAsync(tenantId, cancellationToken)).ToDictionary(type => type.Id, type => type.IsDomainBoundary);
        var links = (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).ToDictionary(link => link.UnitId, link => link.CompanyId);
        return new OrgGraph(
            unitList.Select(unit => new UnitNode(
                unit.Id,
                unit.ParentUnitId,
                unit.ManagerUserId,
                unit.NodeTypeId is { } typeId && boundaryOf.TryGetValue(typeId, out var boundary) ? boundary : true)),
            links);
    }
}
