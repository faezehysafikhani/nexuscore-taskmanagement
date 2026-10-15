using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

public sealed class WorkflowStep : Entity<Guid>
{
    private WorkflowStep() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    internal WorkflowStep(Guid id, Guid workflowDefinitionId, int order, string name, Guid? approverUserId, Guid? approverRoleId, string? approverKey = null, bool isOptional = false) : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        Order = order;
        Name = name.Trim();
        ApproverUserId = approverUserId;
        ApproverRoleId = approverRoleId;
        ApproverKey = string.IsNullOrWhiteSpace(approverKey) ? null : approverKey.Trim();
        IsOptional = isOptional;
    }

    public Guid WorkflowDefinitionId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; }

    /// <summary>A specific designated approver. If null, ApproverRoleId is informational and
    /// any user holding the Workflow.Approve permission may decide this step.</summary>
    public Guid? ApproverUserId { get; private set; }
    public Guid? ApproverRoleId { get; private set; }

    /// <summary>Instead of naming a user, names a participant of the subject ("Manager", "Owner", ...) that is
    /// looked up when the approval starts. Ignored when ApproverUserId is set.</summary>
    public string? ApproverKey { get; private set; }

    /// <summary>With ApproverKey: the step is left out of an approval whose subject has no such participant.</summary>
    public bool IsOptional { get; private set; }

    internal void SetOrder(int order) => Order = order;
}
