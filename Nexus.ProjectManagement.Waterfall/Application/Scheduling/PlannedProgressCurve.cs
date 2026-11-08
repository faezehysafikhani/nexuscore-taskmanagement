using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>
/// How much of the project should be done by a given date, according to the schedule alone: each
/// leaf completes evenly across its working days (a milestone all at once, when it falls due),
/// and groups combine with the same weighting rule as the progress roll-up. This is the
/// "planned" line of the S-curve; it does not depend on the progress people have entered.
/// </summary>
public sealed class PlannedProgressCurve
{
    private readonly PlannedProgressMode _mode;
    private readonly ScheduleResult _schedule;
    private readonly Dictionary<Guid, ScheduledActivity> _byId;
    private readonly Dictionary<Guid, List<Guid>> _childrenOf;
    private readonly List<Guid> _topLevel;
    private readonly Dictionary<Guid, int> _leafDuration = [];

    /// <param name="mode">Continuous (the default; also what Stored means here, since a curve is always calculated) spreads a
    /// leaf's progress evenly over its working days; ZeroHundred keeps it at 0 until its last day is over.</param>
    public PlannedProgressCurve(ScheduleResult schedule, PlannedProgressMode mode = PlannedProgressMode.Continuous)
    {
        _mode = mode;
        _schedule = schedule;
        _byId = schedule.Activities.ToDictionary(a => a.Id);
        _childrenOf = schedule.Activities
            .Where(a => a.ParentId is not null)
            .GroupBy(a => a.ParentId!.Value, a => a.Id)
            .ToDictionary(g => g.Key, g => g.ToList());
        _topLevel = schedule.Activities.Where(a => a.ParentId is null).Select(a => a.Id).ToList();

        foreach (var activity in schedule.Activities)
        {
            LeafDuration(activity.Id);
        }
    }

    /// <summary>Planned progress, 0-100, once the given date is over.</summary>
    public decimal At(DateOnly date)
    {
        var completed = _schedule.Axis.WorkingDaysThrough(date);
        return Group(_topLevel, completed);
    }

    /// <summary>Planned progress of every activity (a group's is the weighted roll-up of its children), once the date is over.</summary>
    public IReadOnlyDictionary<Guid, decimal> AllAt(DateOnly date)
    {
        var completed = _schedule.Axis.WorkingDaysThrough(date);
        return _schedule.Activities.ToDictionary(a => a.Id, a => Of(a.Id, completed));
    }

    private decimal Group(IEnumerable<Guid> ids, int completed) =>
        ScheduleCalculator.Rollup(ids.Select(id => (_byId[id].Weight, _leafDuration[id], Of(id, completed), 0m))).Planned;

    private decimal Of(Guid id, int completed)
    {
        if (_childrenOf.TryGetValue(id, out var children))
        {
            return Group(children, completed);
        }

        var leaf = _byId[id];
        if (leaf.IsMilestone || leaf.DurationDays == 0)
        {
            return completed >= leaf.StartIndex ? 100m : 0m;
        }

        if (_mode == PlannedProgressMode.ZeroHundred)
        {
            return completed >= leaf.FinishIndex ? 100m : 0m;
        }

        var fraction = (decimal)(completed - leaf.StartIndex) / leaf.DurationDays;
        return Math.Clamp(fraction, 0m, 1m) * 100m;
    }

    private int LeafDuration(Guid id)
    {
        if (_leafDuration.TryGetValue(id, out var known))
        {
            return known;
        }

        var total = _childrenOf.TryGetValue(id, out var children)
            ? children.Sum(LeafDuration)
            : _byId[id].DurationDays;
        return _leafDuration[id] = total;
    }
}
