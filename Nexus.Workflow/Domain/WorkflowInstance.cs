using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

public enum WorkflowInstanceStatus
{
    InProgress,
    Approved,
    Rejected
}

/// <summary>
/// One in-flight (or completed) approval run for a single ApprovalSubject. TotalSteps is
/// captured from the definition at creation time so edits to the definition's steps never
/// retroactively change an instance already in progress.
/// </summary>
public sealed class WorkflowInstance : AuditableEntity<Guid>
{
    private readonly List<WorkflowDecision> _decisions = [];

    private WorkflowInstance() : base(Guid.Empty)
    {
        SubjectType = string.Empty;
    }

    public WorkflowInstance(Guid id, Guid tenantId, Guid workflowDefinitionId, string subjectType, Guid subjectId, int totalSteps) : base(id)
    {
        TenantId = tenantId;
        WorkflowDefinitionId = workflowDefinitionId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        TotalSteps = totalSteps;
        CurrentStepOrder = 1;
        Status = WorkflowInstanceStatus.InProgress;
    }

    public Guid TenantId { get; private set; }
    public Guid WorkflowDefinitionId { get; private set; }
    public string SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public int TotalSteps { get; private set; }
    public int CurrentStepOrder { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }
    /// <summary>Not sorted here - _decisions must stay the exact EF-tracked backing collection
    /// so Include() can populate it. Callers that need order should OrderBy(StepOrder).</summary>
    public IReadOnlyCollection<WorkflowDecision> Decisions => _decisions.AsReadOnly();

    /// <summary>Returns the ApprovalGranted event only once the last step has approved.</summary>
    public IDomainEvent? Decide(Guid decidedByUserId, bool approved, string? comment)
    {
        if (Status != WorkflowInstanceStatus.InProgress)
        {
            throw new InvalidOperationException("This workflow instance has already been decided.");
        }

        _decisions.Add(new WorkflowDecision(Guid.NewGuid(), Id, CurrentStepOrder, decidedByUserId, approved, comment));

        if (!approved)
        {
            Status = WorkflowInstanceStatus.Rejected;
            var rejected = new ApprovalRejected(SubjectType, SubjectId, TenantId, decidedByUserId, comment);
            RaiseDomainEvent(rejected);
            return rejected;
        }

        if (CurrentStepOrder >= TotalSteps)
        {
            Status = WorkflowInstanceStatus.Approved;
            var granted = new ApprovalGranted(SubjectType, SubjectId, TenantId, decidedByUserId, comment);
            RaiseDomainEvent(granted);
            return granted;
        }

        CurrentStepOrder++;
        return null;
    }
}
