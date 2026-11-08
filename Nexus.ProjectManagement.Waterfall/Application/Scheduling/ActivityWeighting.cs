using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>What the weighting needs to know about one leaf activity.</summary>
public sealed record WeightingInput(Guid Id, int DurationDays, decimal Cost, ActivityLevel Complexity);

/// <summary>
/// The weight (percent of the whole, summing to 100 over the leaves) each leaf carries when a project weighs by
/// something other than typed weights: its share of the total duration, cost or complexity score, or a blend of the
/// three. A measure whose total is zero contributes nothing, so a project with no costs entered still weighs by time
/// when it is "combined". Pure: it touches no storage.
/// </summary>
public static class ActivityWeighting
{
    public static IReadOnlyDictionary<Guid, decimal> Compute(
        IReadOnlyList<WeightingInput> leaves, WeightingMode mode, int timeFactor, int costFactor, int complexityFactor)
    {
        var result = leaves.ToDictionary(l => l.Id, _ => 0m);
        if (mode == WeightingMode.Manual || leaves.Count == 0)
        {
            return result;
        }

        var totalDuration = leaves.Sum(l => (decimal)l.DurationDays);
        var totalCost = leaves.Sum(l => l.Cost);
        var totalScore = leaves.Sum(l => (decimal)Score(l.Complexity));

        decimal Share(decimal value, decimal total) => total > 0 ? value / total * 100m : 0m;

        foreach (var leaf in leaves)
        {
            var time = Share(leaf.DurationDays, totalDuration);
            var cost = Share(leaf.Cost, totalCost);
            var complexity = Share(Score(leaf.Complexity), totalScore);

            result[leaf.Id] = mode switch
            {
                WeightingMode.Time => time,
                WeightingMode.Cost => cost,
                WeightingMode.Complexity => complexity,
                _ => (time * timeFactor + cost * costFactor + complexity * complexityFactor) / 100m
            };
        }

        return result;
    }

    public static int Score(ActivityLevel level) => level switch
    {
        ActivityLevel.Low => 1,
        ActivityLevel.High => 3,
        _ => 2
    };
}
