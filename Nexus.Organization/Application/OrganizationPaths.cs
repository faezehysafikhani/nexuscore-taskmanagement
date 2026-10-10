using Nexus.Organization.Domain;

namespace Nexus.Organization.Application;

/// <summary>Each unit's full path from the top of the chart ("Organization > Engineering > Civil"). A parent chain that loops is cut where it repeats.</summary>
public static class OrganizationPaths
{
    public const string Separator = " > ";

    public static Dictionary<Guid, (string Name, string Path)> For(IReadOnlyCollection<OrganizationUnit> units)
    {
        var byId = units.ToDictionary(u => u.Id);
        var result = new Dictionary<Guid, (string, string)>();
        foreach (var unit in units)
        {
            var names = new List<string> { unit.Name };
            var seen = new HashSet<Guid> { unit.Id };
            var current = unit;
            while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) && seen.Add(parentId))
            {
                names.Insert(0, parent.Name);
                current = parent;
            }

            result[unit.Id] = (unit.Name, string.Join(Separator, names));
        }

        return result;
    }

    /// <summary>Every unit below <paramref name="unitId"/> (children, their children...), not the unit itself.</summary>
    public static HashSet<Guid> Descendants(IReadOnlyCollection<OrganizationUnit> units, Guid unitId)
    {
        var childrenOf = units.Where(u => u.ParentId is not null).ToLookup(u => u.ParentId!.Value, u => u.Id);
        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>([unitId]);
        while (stack.Count > 0)
        {
            foreach (var child in childrenOf[stack.Pop()])
            {
                if (child != unitId && result.Add(child))
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="candidateParentId"/> is the unit itself or one of its descendants - i.e. making it the parent would loop the chart.</summary>
    public static bool WouldLoop(IReadOnlyCollection<OrganizationUnit> units, Guid unitId, Guid? candidateParentId)
    {
        var byId = units.ToDictionary(u => u.Id);
        var seen = new HashSet<Guid>();
        var current = candidateParentId;
        while (current is { } id && seen.Add(id))
        {
            if (id == unitId)
            {
                return true;
            }

            current = byId.TryGetValue(id, out var unit) ? unit.ParentId : null;
        }

        return false;
    }
}
