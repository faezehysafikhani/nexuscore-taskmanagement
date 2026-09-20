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
    string? Comment) : DomainEvent;

public sealed record ApprovalRejected(
    string SubjectType,
    Guid SubjectId,
    Guid TenantId,
    Guid? DecidedByUserId,
    string? Comment) : DomainEvent;
