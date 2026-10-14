using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Domain;

namespace Nexus.StrategyManagement.Application;

/// <summary>
/// The strategies as a tree: how deep each one is, whether it has sub-strategies and the weight it effectively carries. A strategy with
/// sub-strategies weighs what they add up to (its own stored weight is not used); a leaf weighs what it stores. The weights of the
/// top-level strategies are what has to add up to 100.
/// </summary>
public static class StrategyTree
{
    public static StrategyTreeDto Build(IEnumerable<Strategy> strategies)
    {
        var all = strategies.ToList();
        var byParent = all.Where(s => s.ParentStrategyId is not null).GroupBy(s => s.ParentStrategyId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var ids = all.Select(s => s.Id).ToHashSet();
        var roots = all.Where(s => s.ParentStrategyId is null || !ids.Contains(s.ParentStrategyId.Value)).ToList();
        var nodes = new List<StrategyNodeDto>();

        decimal Walk(Strategy strategy, int level, HashSet<Guid> path)
        {
            // The slot is reserved first so a parent is listed before its children (the weight is filled in afterwards).
            var slot = nodes.Count;
            nodes.Add(null!);
            var children = byParent.TryGetValue(strategy.Id, out var found) ? Order(found) : [];
            decimal weight;
            if (children.Count == 0 || !path.Add(strategy.Id))
            {
                weight = strategy.Weight;
            }
            else
            {
                weight = children.Sum(child => Walk(child, level + 1, path));
                path.Remove(strategy.Id);
            }

            nodes[slot] = new StrategyNodeDto(
                strategy.Id, strategy.Name, strategy.Description, strategy.Weight, weight,
                strategy.ParentStrategyId is { } parent && ids.Contains(parent) ? parent : null,
                level, children.Count == 0, strategy.SortOrder);
            return weight;
        }

        var total = Order(roots).Sum(root => Walk(root, 0, []));
        return new StrategyTreeDto(nodes, total);
    }

    /// <summary>The leaf strategies and the weight each carries, in tree order.</summary>
    public static IReadOnlyList<StrategyNodeDto> Leaves(IEnumerable<Strategy> strategies) => Build(strategies).Nodes.Where(n => n.IsLeaf).ToList();

    private static List<Strategy> Order(IEnumerable<Strategy> strategies) =>
        strategies.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.CurrentCulture).ToList();
}
