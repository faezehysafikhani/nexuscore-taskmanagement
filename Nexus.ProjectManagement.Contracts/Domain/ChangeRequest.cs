using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>What a change is mainly about. Stored as its integer value; never reorder or renumber the members.</summary>
public enum ChangeRequestType
{
    Scope = 0,
    Schedule = 1,
    Cost = 2,
    Quality = 3,
    Other = 4
}

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ChangeRequestPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

/// <summary>
/// The life of a change request: written as a Draft, Submitted for a decision (PendingApproval), then Approved or Rejected; an approved
/// change is later marked Implemented, and one that is no longer wanted is Cancelled. Stored as its integer value; never reorder or renumber.
/// </summary>
public enum ChangeRequestStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Implemented = 4,
    Cancelled = 5
}

/// <summary>
/// A formal request to change a project - its scope, schedule, cost or quality - with the assessment of what the change does to scope, time
/// and cost written down before anyone decides on it. Numbered 1, 2, 3... per project. The decision goes through the same optional approval
/// as the other project records (Workflow when it is installed, otherwise approved as soon as it is submitted); the project history records
/// every edit, since the request carries its ProjectId.
/// </summary>
public sealed class ChangeRequest : AuditableEntity<Guid>
{
    private ChangeRequest() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ChangeRequest(Guid id, Guid tenantId, Guid projectId, int number, string title) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Number = number;
        Title = title.Trim();
        Status = ChangeRequestStatus.Draft;
        Priority = ChangeRequestPriority.Normal;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Why the change is asked for.</summary>
    public string? Reason { get; private set; }

    public ChangeRequestType Type { get; private set; }
    public ChangeRequestPriority Priority { get; private set; }

    /// <summary>Who asked for it, as a name (it may be someone outside the system, such as the client).</summary>
    public string? RequestedBy { get; private set; }

    /// <summary>The contract the change concerns, if it concerns one.</summary>
    public Guid? ContractId { get; private set; }

    // The impact assessment, required before the request can be submitted.
    public string? ScopeImpact { get; private set; }

    /// <summary>Calendar days the change adds to the schedule (negative when it saves time).</summary>
    public int ScheduleImpactDays { get; private set; }

    /// <summary>Rials the change adds to the cost (negative when it saves money).</summary>
    public decimal CostImpact { get; private set; }

    /// <summary>Other effects: quality, risks, resources.</summary>
    public string? OtherImpact { get; private set; }

    public ChangeRequestStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>What the approver wrote with the decision, when there was a Workflow decision.</summary>
    public string? DecisionNote { get; private set; }

    public DateOnly? ImplementedDate { get; private set; }

    /// <summary>A short note on the outcome: why it was cancelled, or how it was carried out.</summary>
    public string? OutcomeNote { get; private set; }

    public bool HasImpactAssessment => !string.IsNullOrWhiteSpace(ScopeImpact);

    public void Update(
        string title, string? description, string? reason, ChangeRequestType type, ChangeRequestPriority priority, string? requestedBy,
        Guid? contractId, string? scopeImpact, int scheduleImpactDays, decimal costImpact, string? otherImpact)
    {
        Title = title.Trim();
        Description = Clean(description);
        Reason = Clean(reason);
        Type = type;
        Priority = priority;
        RequestedBy = Clean(requestedBy);
        ContractId = contractId;
        ScopeImpact = Clean(scopeImpact);
        ScheduleImpactDays = scheduleImpactDays;
        CostImpact = costImpact;
        OtherImpact = Clean(otherImpact);

        // A rejected request that is reworked goes back to being a draft, to be submitted again.
        if (Status == ChangeRequestStatus.Rejected)
        {
            Status = ChangeRequestStatus.Draft;
            DecidedAtUtc = null;
            DecisionNote = null;
        }
    }

    public void MarkSubmitted(DateTimeOffset now)
    {
        Status = ChangeRequestStatus.PendingApproval;
        SubmittedAtUtc = now;
    }

    public void Approve(DateTimeOffset now, string? note = null)
    {
        Status = ChangeRequestStatus.Approved;
        DecidedAtUtc = now;
        DecisionNote = Clean(note);
    }

    public void Reject(DateTimeOffset now, string? note = null)
    {
        Status = ChangeRequestStatus.Rejected;
        DecidedAtUtc = now;
        DecisionNote = Clean(note);
    }

    public void MarkImplemented(DateOnly date, string? note)
    {
        Status = ChangeRequestStatus.Implemented;
        ImplementedDate = date;
        OutcomeNote = Clean(note);
    }

    public void Cancel(string? note)
    {
        Status = ChangeRequestStatus.Cancelled;
        OutcomeNote = Clean(note);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
