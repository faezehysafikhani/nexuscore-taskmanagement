using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

public sealed class WorkflowStep : Entity<Guid>
{
    private WorkflowStep() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    internal WorkflowStep(Guid id, Guid workflowDefinitionId, int order, string name, Guid? approverUserId, Guid? approverRoleId) : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        Order = order;
        Name = name.Trim();
        ApproverUserId = approverUserId;
        ApproverRoleId = approverRoleId;
    }

    public Guid WorkflowDefinitionId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; }

    /// <summary>A specific designated approver. If null, ApproverRoleId is informational and
    /// any user holding the Workflow.Approve permission may decide this step.</summary>
    public Guid? ApproverUserId { get; private set; }
    public Guid? ApproverRoleId { get; private set; }

    internal void SetOrder(int order) => Order = order;
}
