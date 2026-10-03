using Nexus.Organization.Domain;

namespace Nexus.Reporting.Application.Analytics;

/// <summary>
/// Reads the organisation chart as levels: roots are level 1, their children level 2, and so on. A
/// project assigned to a unit deeper than the level asked for rolls up into that unit's ancestor at
/// the level; one assigned to a unit at or above it stays with its own unit. A parent chain that loops
/// is cut where it repeats, so bad data cannot hang a report.
/// </summary>
internal sealed class OrganizationRollup
{
    private readonly Dictionary<Guid, OrganizationUnit> _units;
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _paths = [];

    public OrganizationRollup(IEnumerable<OrganizationUnit> units)
    {
        _units = units.ToDictionary(u => u.Id);
        foreach (var unit in _units.Values)
        {
            _paths[unit.Id] = PathTo(unit);
        }

        MaxLevel = _paths.Count == 0 ? 0 : _paths.Values.Max(p => p.Count);
    }

    public int MaxLevel { get; }

    public OrganizationUnit? Find(Guid id) => _units.GetValueOrDefault(id);

    public IEnumerable<OrganizationUnit> Units => _units.Values;

    /// <summary>1 for a root unit; 0 for an unknown one.</summary>
    public int DepthOf(Guid unitId) => _paths.TryGetValue(unitId, out var path) ? path.Count : 0;

    /// <summary>The unit a project assigned to <paramref name="unitId"/> is counted under at <paramref name="level"/>; null for none or unknown.</summary>
    public Guid? RowFor(Guid? unitId, int level)
    {
        if (unitId is null || !_paths.TryGetValue(unitId.Value, out var path))
        {
            return null;
        }

        return path[Math.Min(level, path.Count) - 1];
    }

    private List<Guid> PathTo(OrganizationUnit unit)
    {
        var path = new List<Guid> { unit.Id };
        var seen = new HashSet<Guid> { unit.Id };
        var current = unit;
        while (current.ParentId is { } parentId && _units.TryGetValue(parentId, out var parent) && seen.Add(parentId))
        {
            path.Insert(0, parentId);
            current = parent;
        }

        return path;
    }
}
