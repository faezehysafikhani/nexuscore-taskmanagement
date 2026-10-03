using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

/// <summary>
/// One person handing their approvals to another for a period (a holiday, a mission): for the days
/// from StartDate to EndDate inclusive, the delegate sees the delegator's pending approvals in their
/// Approval Center and a decision they make there is recorded as made on the delegator's behalf.
/// SubjectType, when set, limits it to one kind of approval ("Risk"); null covers all of them.
/// A delegation is ended early by revoking it, never deleted - the record of who could act stays.
/// Generic like the rest of Workflow: it knows nothing about what is being approved.
/// </summary>
public sealed class WorkflowDelegation : AuditableEntity<Guid>
{
    private WorkflowDelegation() : base(Guid.Empty)
    {
    }

    public WorkflowDelegation(
        Guid id, Guid tenantId, Guid delegatorUserId, Guid delegateUserId,
        DateOnly startDate, DateOnly endDate, string? subjectType, string? reason) : base(id)
    {
        TenantId = tenantId;
        DelegatorUserId = delegatorUserId;
        DelegateUserId = delegateUserId;
        StartDate = startDate;
        EndDate = endDate;
        SubjectType = string.IsNullOrWhiteSpace(subjectType) ? null : subjectType.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public Guid TenantId { get; private set; }

    /// <summary>The approver whose work is handed over.</summary>
    public Guid DelegatorUserId { get; private set; }

    /// <summary>The substitute who may act for them.</summary>
    public Guid DelegateUserId { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? SubjectType { get; private set; }
    public string? Reason { get; private set; }
    public bool IsRevoked { get; private set; }

    public bool IsActiveOn(DateOnly date) => !IsRevoked && date >= StartDate && date <= EndDate;

    public bool Covers(string subjectType) => SubjectType is null || SubjectType == subjectType;

    public void Revoke() => IsRevoked = true;
}
