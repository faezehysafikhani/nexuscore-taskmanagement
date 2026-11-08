namespace Nexus.ProjectManagement.Waterfall;

/// <summary>Opt-in behaviour of the Waterfall module. Every switch defaults to off, so a project that
/// already uses the module sees exactly what it always saw.</summary>
public sealed class WaterfallOptions
{
    /// <summary>When on, creating an activity or changing its parent checks the hierarchy: the parent must be
    /// an activity of the same project, not a milestone, not the activity itself or one of its own descendants,
    /// and must not already carry dependency links. Off (the default) accepts any parent id, as before.</summary>
    public bool ValidateActivityHierarchy { get; set; }

    /// <summary>How planned progress is read for a project that has not chosen a mode of its own. Stored (the default) uses the
    /// value stored on each activity, as before; Continuous / ZeroHundred calculate it from the schedule.</summary>
    public Domain.PlannedProgressMode DefaultPlannedProgress { get; set; } = Domain.PlannedProgressMode.Stored;

    /// <summary>When on, a group's weight in the roll-up is the sum of its children's weights (so a group counts for exactly as
    /// much as the work inside it). Off (the default) uses the weight stored on the group itself, as before. A project that weighs
    /// by time, cost, complexity or a blend always rolls up this way.</summary>
    public bool RollUpSummaryWeights { get; set; }

    /// <summary>When on, a waterfall project that weighs its activities by hand cannot be sent for approval unless the weights of its
    /// activities add up to 100 (error code <c>project.schedule_weights_not_100</c>). Off (the default) lets any weights through, as before.</summary>
    public bool RequireWeightsTotalOnSubmit { get; set; }
}
