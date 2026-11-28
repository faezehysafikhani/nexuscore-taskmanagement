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

    /// <summary>
    /// Subject types whose submitter may never decide any step of their own approval. When a step would be theirs, it
    /// goes to their manager in the organization chart (OrgManager); with no such manager the submission is refused.
    /// Project charters and the project registers (risks, stakeholders, status updates, delay reasons, documents) by default.
    /// </summary>
    public ISet<string> SelfApprovalForbiddenSubjectTypes { get; } = new HashSet<string>(StringComparer.Ordinal) { "Project", "Risk", "Stakeholder", "ProgressUpdate", "DelayReason", "ProjectDocument" };
}
