namespace Nexus.Workflow;

/// <summary>
/// Opt-in behaviour of the approval backend. Every switch defaults to the behaviour Workflow had before it
/// existed, so a host that does not touch the options sees no change.
/// </summary>
public sealed class WorkflowOptions
{
    /// <summary>
    /// When true, a step that has a designated approver (a named user, or a participant the subject resolved to)
    /// can only be decided by that person or by someone they have delegated to - plus holders of Workflow.Configure,
    /// who can always step in. When false (default) anyone holding Workflow.Approve/Reject may decide any step, and the
    /// designated approver only decides who sees the item in their approval centre.
    /// </summary>
    public bool RequireDesignatedApprover { get; set; }
}
