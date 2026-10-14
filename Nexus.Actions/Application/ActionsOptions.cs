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
}
