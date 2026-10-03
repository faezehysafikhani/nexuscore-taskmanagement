namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>Cycle detection over predecessor -> successor links, shared by link creation, the
/// schedule calculation and the MS Project import so they all agree on what a cycle is.</summary>
public static class DependencyGraph
{
    /// <summary>True when adding predecessor -> successor to <paramref name="existingLinks"/>
    /// would let the successor reach back to the predecessor (including a self-link).</summary>
    public static bool WouldCreateCycle(
        IEnumerable<(Guid Predecessor, Guid Successor)> existingLinks, Guid predecessor, Guid successor)
    {
        if (predecessor == successor)
        {
            return true;
        }

        var successorsOf = existingLinks
            .GroupBy(link => link.Predecessor, link => link.Successor)
            .ToDictionary(group => group.Key, group => group.ToList());

        // Walk forward from the proposed successor; reaching the proposed predecessor closes a loop.
        var seen = new HashSet<Guid> { successor };
        var stack = new Stack<Guid>();
        stack.Push(successor);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!successorsOf.TryGetValue(node, out var next))
            {
                continue;
            }

            foreach (var candidate in next)
            {
                if (candidate == predecessor)
                {
                    return true;
                }

                if (seen.Add(candidate))
                {
                    stack.Push(candidate);
                }
            }
        }

        return false;
    }

    /// <summary>The nodes in an order where every predecessor precedes its successors, or null
    /// when the links contain a cycle.</summary>
    public static IReadOnlyList<Guid>? TopologicalOrder(IEnumerable<Guid> nodes, IEnumerable<(Guid Predecessor, Guid Successor)> links)
    {
        var all = nodes.ToList();
        var known = all.ToHashSet();
        var incoming = all.ToDictionary(node => node, _ => 0);
        var outgoing = all.ToDictionary(node => node, _ => new List<Guid>());

        foreach (var (predecessor, successor) in links)
        {
            if (!known.Contains(predecessor) || !known.Contains(successor))
            {
                continue;
            }

            outgoing[predecessor].Add(successor);
            incoming[successor]++;
        }

        var ready = new Queue<Guid>(all.Where(node => incoming[node] == 0));
        var order = new List<Guid>(all.Count);
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            order.Add(node);
            foreach (var next in outgoing[node])
            {
                if (--incoming[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        return order.Count == all.Count ? order : null;
    }
}
