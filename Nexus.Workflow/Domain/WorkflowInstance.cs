using System.Text.Json;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

public enum WorkflowInstanceStatus
{
    InProgress,
    Approved,
    Rejected,
    /// <summary>Taken back before it was decided (the submitting module could not record the submission).</summary>
    Withdrawn
}

/// <summary>One step as this approval runs it: the definition's step with its approver already resolved.</summary>
public sealed record WorkflowInstanceStep(int Order, string Name, Guid? ApproverUserId);

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
        Initialize(tenantId, workflowDefinitionId, subjectType, subjectId, totalSteps);
    }

    /// <summary>Starts an approval with the steps (and their resolved approvers) captured as they are now: a
    /// later edit of the definition, or of the people behind a placeholder, does not move an approval in flight.</summary>
    public WorkflowInstance(Guid id, Guid tenantId, Guid workflowDefinitionId, string subjectType, Guid subjectId, IReadOnlyList<WorkflowInstanceStep> steps,
        Guid? submittedByUserId = null)
        : base(id)
    {
        Initialize(tenantId, workflowDefinitionId, subjectType, subjectId, steps.Count);
        StepsSnapshot = JsonSerializer.Serialize(steps);
        SubmittedByUserId = submittedByUserId;
        RaiseStepAssigned();
    }

    private void Initialize(Guid tenantId, Guid workflowDefinitionId, string subjectType, Guid subjectId, int totalSteps)
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

    /// <summary>Who sent the subject for approval (null on approvals started before this was recorded, or by the system).</summary>
    public Guid? SubmittedByUserId { get; private set; }

    /// <summary>
    /// Concurrency token: changes with every decision, so two decisions on the same step at the same time cannot both
    /// be saved - the second gets a conflict. Null on approvals started before it existed (still checked: the first
    /// decision sets it).
    /// </summary>
    public Guid? RowVersion { get; private set; }

    /// <summary>The steps this approval runs, as JSON (null on approvals started before this existed - they read
    /// the definition's steps instead).</summary>
    public string? StepsSnapshot { get; private set; }

    public IReadOnlyList<WorkflowInstanceStep>? GetSteps() =>
        string.IsNullOrWhiteSpace(StepsSnapshot) ? null : JsonSerializer.Deserialize<List<WorkflowInstanceStep>>(StepsSnapshot);
    /// <summary>Not sorted here - _decisions must stay the exact EF-tracked backing collection
    /// so Include() can populate it. Callers that need order should OrderBy(StepOrder).</summary>
    public IReadOnlyCollection<WorkflowDecision> Decisions => _decisions.AsReadOnly();

    /// <summary>Returns the ApprovalGranted event only once the last step has approved.</summary>
    public IDomainEvent? Decide(Guid decidedByUserId, bool approved, string? comment, Guid? onBehalfOfUserId = null)
    {
        if (Status != WorkflowInstanceStatus.InProgress)
        {
            throw new InvalidOperationException("This workflow instance has already been decided.");
        }

        _decisions.Add(new WorkflowDecision(Guid.NewGuid(), Id, CurrentStepOrder, decidedByUserId, approved, comment, onBehalfOfUserId));
        RowVersion = Guid.NewGuid();

        if (!approved)
        {
            Status = WorkflowInstanceStatus.Rejected;
            var rejected = new ApprovalRejected(SubjectType, SubjectId, TenantId, decidedByUserId, comment, SubmittedByUserId);
            RaiseDomainEvent(rejected);
            return rejected;
        }

        if (CurrentStepOrder >= TotalSteps)
        {
            Status = WorkflowInstanceStatus.Approved;
            var granted = new ApprovalGranted(SubjectType, SubjectId, TenantId, decidedByUserId, comment, SubmittedByUserId);
            RaiseDomainEvent(granted);
            return granted;
        }

        CurrentStepOrder++;
        RaiseStepAssigned();
        return null;
    }

    /// <summary>Takes back an approval still in progress (no decision is recorded). False when it was already decided.</summary>
    public bool Withdraw()
    {
        if (Status != WorkflowInstanceStatus.InProgress)
        {
            return false;
        }

        Status = WorkflowInstanceStatus.Withdrawn;
        RowVersion = Guid.NewGuid();
        return true;
    }

    /// <summary>The step now waiting, for whoever tells its approver (only known when the steps were captured).</summary>
    private void RaiseStepAssigned()
    {
        var step = GetSteps()?.SingleOrDefault(s => s.Order == CurrentStepOrder);
        if (step is not null)
        {
            RaiseDomainEvent(new ApprovalStepAssigned(SubjectType, SubjectId, TenantId, step.Order, step.Name, step.ApproverUserId, SubmittedByUserId));
        }
    }
}
