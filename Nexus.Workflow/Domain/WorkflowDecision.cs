using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

public sealed class WorkflowDecision : Entity<Guid>
{
    private WorkflowDecision() : base(Guid.Empty)
    {
    }

    internal WorkflowDecision(Guid id, Guid workflowInstanceId, int stepOrder, Guid decidedByUserId, bool approved, string? comment) : base(id)
    {
        WorkflowInstanceId = workflowInstanceId;
        StepOrder = stepOrder;
        DecidedByUserId = decidedByUserId;
        Approved = approved;
        Comment = comment;
        DecidedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid WorkflowInstanceId { get; private set; }
    public int StepOrder { get; private set; }
    public Guid DecidedByUserId { get; private set; }
    public bool Approved { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }
}
