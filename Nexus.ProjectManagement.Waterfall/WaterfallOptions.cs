namespace Nexus.ProjectManagement.Waterfall;

/// <summary>Opt-in behaviour of the Waterfall module. Every switch defaults to off, so a project that
/// already uses the module sees exactly what it always saw.</summary>
public sealed class WaterfallOptions
{
    /// <summary>When on, creating an activity or changing its parent checks the hierarchy: the parent must be
    /// an activity of the same project, not a milestone, not the activity itself or one of its own descendants,
    /// and must not already carry dependency links. Off (the default) accepts any parent id, as before.</summary>
    public bool ValidateActivityHierarchy { get; set; }
}
