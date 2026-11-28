using NexusCore.SharedKernel.Domain;

namespace NexusCore.Application.Approvals;

/// <summary>
/// Raised by whatever approval backend is installed (typically Workflow, via a WorkflowInstance
/// decision) when a subject is approved. A module that submitted an ApprovalSubject with a
/// matching SubjectType reacts by implementing IDomainEventHandler&lt;ApprovalGranted&gt; and
/// filtering on SubjectType/SubjectId - it never references the approval backend directly.
/// </summary>
public sealed record ApprovalGranted(
    string SubjectType,
    Guid SubjectId,
    Guid TenantId,
    Guid? DecidedByUserId,
    string? Comment,
    Guid? SubmittedByUserId = null) : DomainEvent;

public sealed record ApprovalRejected(
    string SubjectType,
    Guid SubjectId,
    Guid TenantId,
    Guid? DecidedByUserId,
    string? Comment,
    Guid? SubmittedByUserId = null) : DomainEvent;

/// <summary>
/// Raised by the approval backend when a step of a subject's approval starts and waits for someone: when the
/// approval is started (its first step) and each time a step approves and the next one begins. ApproverUserId is
/// null for a step anyone with the approve permission may decide. Used to tell the approver (notifications).
/// </summary>
public sealed record ApprovalStepAssigned(
    string SubjectType,
    Guid SubjectId,
    Guid TenantId,
    int StepOrder,
    string StepName,
    Guid? ApproverUserId,
    Guid? SubmittedByUserId) : DomainEvent;
