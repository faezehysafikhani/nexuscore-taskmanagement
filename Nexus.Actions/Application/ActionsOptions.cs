namespace Nexus.Actions.Application;

/// <summary>Host switches. Every default keeps what the module did before the switch existed.</summary>
public sealed class ActionsOptions
{
    /// <summary>
    /// When true (the default), completing a repeating action creates the next occurrence as a new action (the "chain" model).
    /// A host that keeps all occurrences inside one action - listed from its rule and date range by
    /// <c>GET /api/actions/{id}/occurrences</c> - turns this off.
    /// </summary>
    public bool CreateNextOccurrenceOnCompletion { get; set; } = true;

    /// <summary>
    /// When true, an action whose start and end dates are both given must end after it starts (`action.end_not_after_start`). Off by default.
    /// An edit that leaves the dates as they were is never refused, so actions saved before the rule keep working.
    /// </summary>
    public bool RequireEndAfterStart { get; set; }
}
