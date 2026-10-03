using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>
/// Critical-path scheduling of one project's activities. Pure: it takes plain inputs and returns
/// a result, touching no storage, so the same code serves the schedule endpoint, "apply
/// schedule", baselines, the S-curve and the MS Project import.
///
/// Time model. Everything is computed on the working-day axis (see <see cref="WorkingDayAxis"/>):
/// a leaf occupies [S, S+d), so S is the start of its first working day and S+d the end of its
/// last. Each dependency then becomes one inequality on the successor j of predecessor i, with
/// lag L (working days):
///   finish-to-start   S_j >= F_i + L
///   start-to-start    S_j >= S_i + L
///   finish-to-finish  F_j >= F_i + L   i.e. S_j >= F_i + L - d_j
///   start-to-finish   F_j >= S_i + L   i.e. S_j >= S_i + L - d_j
/// The forward pass takes the largest bound; the backward pass, from the project finish, gives
/// each activity its latest allowable dates; the difference is its total float, and the
/// zero-float activities are the critical path.
///
/// Anchors. An activity with no predecessor starts on its own start date if it has one, else on
/// the project's start. An activity with predecessors is driven entirely by them - its stored
/// dates are ignored - so applying a schedule and recalculating later gives the same answer and
/// shortening a predecessor really does pull its successors earlier.
///
/// Milestones take no time; they are placed at the end of the working day the work before them
/// finishes (the MS Project convention), and an anchored milestone keeps its own date.
/// </summary>
public static class ScheduleCalculator
{
    public const int MaxDurationDays = 36_500;

    public static Result<ScheduleResult> Compute(
        IReadOnlyList<ScheduleActivityInput> activities,
        IReadOnlyList<ScheduleLinkInput> links,
        IWorkingDayCalendar calendar,
        DateOnly? projectStart,
        DateOnly today)
    {
        try
        {
            return ComputeCore(activities, links, calendar, projectStart, today);
        }
        catch (InvalidOperationException exception)
        {
            // The working-day axis refuses a calendar with no working days or a horizon beyond its range.
            return Result.Failure<ScheduleResult>(Error.Validation(exception.Message));
        }
    }

    private static Result<ScheduleResult> ComputeCore(
        IReadOnlyList<ScheduleActivityInput> activities,
        IReadOnlyList<ScheduleLinkInput> links,
        IWorkingDayCalendar calendar,
        DateOnly? projectStart,
        DateOnly today)
    {
        if (activities.Any(a => a.DurationDays > MaxDurationDays))
        {
            return Result.Failure<ScheduleResult>(Error.Validation($"An activity's duration cannot exceed {MaxDurationDays} days."));
        }

        var byId = activities.ToDictionary(a => a.Id);
        var parentOf = EffectiveParents(activities, byId);
        var childrenOf = activities
            .Where(a => parentOf[a.Id] is not null)
            .GroupBy(a => parentOf[a.Id]!.Value, a => a.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
        var leaves = activities.Where(a => !childrenOf.ContainsKey(a.Id)).ToList();

        // The axis must begin at or before every date that matters.
        var dates = activities.SelectMany(a => new[] { a.StartDate, a.EndDate }).Append(projectStart)
            .Where(date => date.HasValue).Select(date => date!.Value).ToList();
        var baseDate = dates.Count > 0 ? dates.Min() : today;
        var axis = new WorkingDayAxis(calendar, baseDate);
        var projectStartIndex = axis.IndexOf(projectStart ?? baseDate);

        var duration = new Dictionary<Guid, int>();
        var defaulted = new HashSet<Guid>();
        foreach (var leaf in leaves)
        {
            if (leaf.IsMilestone)
            {
                duration[leaf.Id] = 0;
            }
            else if (leaf.DurationDays is { } days)
            {
                duration[leaf.Id] = Math.Max(days, 0);
            }
            else if (leaf.StartDate is { } firstDay && leaf.EndDate is { } lastDay)
            {
                duration[leaf.Id] = Math.Max(axis.WorkingDaysBetween(firstDay, lastDay), 1);
            }
            else
            {
                duration[leaf.Id] = 1;
                defaulted.Add(leaf.Id);
            }
        }

        var leafIds = leaves.Select(l => l.Id).ToHashSet();
        var usable = links.Where(l => l.PredecessorId != l.SuccessorId && leafIds.Contains(l.PredecessorId) && leafIds.Contains(l.SuccessorId)).ToList();
        var order = DependencyGraph.TopologicalOrder(leaves.Select(l => l.Id), usable.Select(l => (l.PredecessorId, l.SuccessorId)));
        if (order is null)
        {
            return Result.Failure<ScheduleResult>(Error.Conflict("The dependencies contain a circular reference."));
        }

        var incoming = usable.GroupBy(l => l.SuccessorId).ToDictionary(g => g.Key, g => g.ToList());
        var outgoing = usable.GroupBy(l => l.PredecessorId).ToDictionary(g => g.Key, g => g.ToList());

        // ---- forward pass: earliest start (S) and finish (F) of every leaf
        var start = new Dictionary<Guid, int>();
        var finish = new Dictionary<Guid, int>();
        foreach (var id in order)
        {
            var activity = byId[id];
            var length = duration[id];
            int s;
            if (!incoming.TryGetValue(id, out var inbound))
            {
                var anchor = activity.StartDate is { } own ? axis.IndexOf(own) : projectStartIndex;
                s = activity.IsMilestone ? anchor + 1 : anchor;
            }
            else
            {
                s = inbound.Max(link => link.Type switch
                {
                    DependencyType.FinishToStart => finish[link.PredecessorId] + link.LagDays,
                    DependencyType.StartToStart => start[link.PredecessorId] + link.LagDays,
                    DependencyType.FinishToFinish => finish[link.PredecessorId] + link.LagDays - length,
                    _ => start[link.PredecessorId] + link.LagDays - length
                });
                s = Math.Max(s, activity.IsMilestone ? 1 : 0);
            }

            start[id] = s;
            finish[id] = s + length;
        }

        var projectFinishIndex = leaves.Count == 0 ? 0 : leaves.Max(l => finish[l.Id]);

        // ---- backward pass: latest start/finish that still meets the project finish
        var lateStart = new Dictionary<Guid, int>();
        var lateFinish = new Dictionary<Guid, int>();
        foreach (var id in order.Reverse())
        {
            var length = duration[id];
            var lf = projectFinishIndex;
            if (outgoing.TryGetValue(id, out var outbound))
            {
                foreach (var link in outbound)
                {
                    var j = link.SuccessorId;
                    lf = Math.Min(lf, link.Type switch
                    {
                        DependencyType.FinishToStart => lateStart[j] - link.LagDays,
                        DependencyType.StartToStart => lateStart[j] - link.LagDays + length,
                        DependencyType.FinishToFinish => lateFinish[j] - link.LagDays,
                        _ => lateFinish[j] - link.LagDays + length
                    });
                }
            }

            lateFinish[id] = lf;
            lateStart[id] = lf - length;
        }

        // ---- results per leaf
        var scheduled = new Dictionary<Guid, ScheduledActivity>();
        foreach (var leaf in leaves)
        {
            var id = leaf.Id;
            var length = duration[id];
            var floatDays = Math.Max(lateStart[id] - start[id], 0);

            DateOnly DateOfStart(int index) => leaf.IsMilestone ? axis.DateAt(index - 1) : axis.DateAt(index);
            DateOnly DateOfFinish(int index) => axis.DateAt(index - 1);

            scheduled[id] = new ScheduledActivity(
                id, parentOf[id], leaf.Name, IsSummary: false, leaf.IsMilestone,
                Start: DateOfStart(start[id]), Finish: leaf.IsMilestone ? DateOfStart(start[id]) : DateOfFinish(finish[id]), length,
                LateStart: DateOfStart(Math.Max(lateStart[id], leaf.IsMilestone ? 1 : 0)),
                LateFinish: leaf.IsMilestone ? DateOfStart(Math.Max(lateStart[id], 1)) : DateOfFinish(lateFinish[id]),
                floatDays, IsCritical: floatDays == 0,
                leaf.PlannedProgress, leaf.ActualProgress,
                start[id], finish[id], defaulted.Contains(id));
        }

        // ---- summaries, deepest first so each one sees finished children; also the progress roll-up
        var leafDuration = new Dictionary<Guid, int>(duration);
        var depth = activities.ToDictionary(a => a.Id, a => Depth(a.Id, parentOf));
        foreach (var node in activities.Where(a => childrenOf.ContainsKey(a.Id)).OrderByDescending(a => depth[a.Id]))
        {
            var children = childrenOf[node.Id].Select(childId => scheduled[childId]).ToList();
            var (planned, actual) = Rollup(childrenOf[node.Id].Select(childId =>
                (byId[childId].Weight, leafDuration[childId], scheduled[childId].PlannedProgress, scheduled[childId].ActualProgress)));
            leafDuration[node.Id] = childrenOf[node.Id].Sum(childId => leafDuration[childId]);

            var first = children.Min(c => c.Start);
            var last = children.Max(c => c.Finish);
            scheduled[node.Id] = new ScheduledActivity(
                node.Id, parentOf[node.Id], node.Name, IsSummary: true, IsMilestone: false,
                first, last, axis.WorkingDaysBetween(first, last),
                children.Min(c => c.LateStart), children.Max(c => c.LateFinish),
                children.Min(c => c.TotalFloatDays), children.Any(c => c.IsCritical),
                planned, actual,
                children.Min(c => c.StartIndex), children.Max(c => c.FinishIndex), UsedDefaultDuration: false);
        }

        var ordered = scheduled.Values.OrderBy(a => a.StartIndex).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
        var criticalPath = ordered.Where(a => !a.IsSummary && a.IsCritical).Select(a => a.Id).ToList();

        var topLevel = activities.Where(a => parentOf[a.Id] is null).ToList();
        var (projectPlanned, projectActual) = Rollup(topLevel.Select(a =>
            (a.Weight, leafDuration[a.Id], scheduled[a.Id].PlannedProgress, scheduled[a.Id].ActualProgress)));

        var projectBegin = ordered.Count > 0 ? ordered.Min(a => a.Start) : axis.DateAt(projectStartIndex);
        var projectEnd = ordered.Count > 0 ? ordered.Max(a => a.Finish) : projectBegin;
        return Result.Success(new ScheduleResult(
            projectBegin, projectEnd, axis.WorkingDaysBetween(projectBegin, projectEnd),
            ordered, criticalPath, projectPlanned, projectActual, axis));
    }

    /// <summary>
    /// Progress of a group of siblings. Weighted by their stated weights when any is given;
    /// otherwise by duration (the "physical weight"); otherwise equally - so a group never
    /// divides by zero and always produces a number.
    /// </summary>
    public static (decimal Planned, decimal Actual) Rollup(IEnumerable<(decimal Weight, int Duration, decimal Planned, decimal Actual)> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return (0, 0);
        }

        Func<(decimal Weight, int Duration, decimal Planned, decimal Actual), decimal> weightOf =
            list.Sum(i => i.Weight) > 0 ? i => i.Weight
            : list.Sum(i => i.Duration) > 0 ? i => i.Duration
            : _ => 1m;

        var total = list.Sum(weightOf);
        return (
            Math.Round(list.Sum(i => weightOf(i) * i.Planned) / total, 2),
            Math.Round(list.Sum(i => weightOf(i) * i.Actual) / total, 2));
    }

    /// <summary>Each activity's parent, with unknown parents and any loop in stored data broken
    /// by treating the offending activity as top-level, so the tree is always a tree.</summary>
    private static Dictionary<Guid, Guid?> EffectiveParents(IReadOnlyList<ScheduleActivityInput> activities, Dictionary<Guid, ScheduleActivityInput> byId)
    {
        var parentOf = activities.ToDictionary(a => a.Id, a => a.ParentId is { } p && p != a.Id && byId.ContainsKey(p) ? (Guid?)p : null);

        foreach (var activity in activities)
        {
            var seen = new HashSet<Guid> { activity.Id };
            for (var cursor = parentOf[activity.Id]; cursor is { } next; cursor = parentOf[next])
            {
                if (next == activity.Id)
                {
                    parentOf[activity.Id] = null;
                    break;
                }

                if (!seen.Add(next))
                {
                    break; // a loop that does not include this activity; its own turn will break it
                }
            }
        }

        return parentOf;
    }

    private static int Depth(Guid id, Dictionary<Guid, Guid?> parentOf)
    {
        var depth = 0;
        for (var cursor = parentOf[id]; cursor is { } next; cursor = parentOf[next])
        {
            depth++;
        }

        return depth;
    }
}
