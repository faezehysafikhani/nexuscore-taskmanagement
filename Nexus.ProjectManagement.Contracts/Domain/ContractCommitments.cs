using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ContractMilestoneStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ObligationStatus
{
    Open = 0,
    Fulfilled = 1,
    Breached = 2
}

/// <summary>Who owes an obligation. Stored as its integer value; never reorder or renumber the members.</summary>
public enum ObligationParty
{
    Supplier = 0,
    Employer = 1
}

/// <summary>
/// A dated point of a contract the other party has to reach (a delivery, an acceptance, a stage of the works). It is Completed on a
/// date, or it is overdue once its due date has passed without that. Milestones carry no money: what is billed stays on the invoices.
/// </summary>
public sealed class ContractMilestone : AuditableEntity<Guid>
{
    private ContractMilestone() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ContractMilestone(Guid id, Guid tenantId, Guid contractId, string title, DateOnly dueDate) : base(id)
    {
        TenantId = tenantId;
        ContractId = contractId;
        Title = title.Trim();
        DueDate = dueDate;
        Status = ContractMilestoneStatus.NotStarted;
    }

    public Guid TenantId { get; private set; }
    public Guid ContractId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public DateOnly DueDate { get; private set; }
    public ContractMilestoneStatus Status { get; private set; }

    /// <summary>The day it was reached; only a Completed milestone has one.</summary>
    public DateOnly? CompletedDate { get; private set; }

    public void Update(string title, string? description, DateOnly dueDate, ContractMilestoneStatus status, DateOnly? completedDate, DateOnly today)
    {
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DueDate = dueDate;
        Status = status;
        CompletedDate = status == ContractMilestoneStatus.Completed ? completedDate ?? today : null;
    }

    public bool IsOverdue(DateOnly today) => Status != ContractMilestoneStatus.Completed && DueDate < today;
}

/// <summary>
/// Something a contract binds one of its parties to (deliver documents, insure the site, pay within a period). Open until it is
/// Fulfilled on a date, or marked Breached. Only an open obligation with a past due date is overdue.
/// </summary>
public sealed class ContractObligation : AuditableEntity<Guid>
{
    private ContractObligation() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ContractObligation(Guid id, Guid tenantId, Guid contractId, string title, ObligationParty party) : base(id)
    {
        TenantId = tenantId;
        ContractId = contractId;
        Title = title.Trim();
        Party = party;
        Status = ObligationStatus.Open;
    }

    public Guid TenantId { get; private set; }
    public Guid ContractId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public ObligationParty Party { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public ObligationStatus Status { get; private set; }

    /// <summary>The day it was met; only a Fulfilled obligation has one.</summary>
    public DateOnly? FulfilledDate { get; private set; }

    public void Update(
        string title, string? description, ObligationParty party, DateOnly? dueDate, ObligationStatus status, DateOnly? fulfilledDate, DateOnly today)
    {
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Party = party;
        DueDate = dueDate;
        Status = status;
        FulfilledDate = status == ObligationStatus.Fulfilled ? fulfilledDate ?? today : null;
    }

    public bool IsOverdue(DateOnly today) => Status == ObligationStatus.Open && DueDate is { } due && due < today;
}
