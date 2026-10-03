using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ContractStatus
{
    Draft = 0,
    Active = 1,
    Completed = 2,
    Terminated = 3
}

/// <summary>
/// A contract signed for a project: who with, for how much (in rials) and over what period.
/// ContractNumber is held with plain 0-9 digits whichever script it was typed in, so "۱۴۰۵/۱۲"
/// and "1405/12" are the same number. Its value is the original amount plus the approved
/// addenda; invoices are billed against it. Same optional-approval pattern as the other
/// project records: nothing counts as money until the contract is approved.
/// </summary>
public sealed class Contract : AuditableEntity<Guid>
{
    private Contract() : base(Guid.Empty)
    {
        ContractNumber = string.Empty;
        Title = string.Empty;
        Counterparty = string.Empty;
    }

    public Contract(Guid id, Guid tenantId, Guid projectId, string contractNumber, string title, string counterparty, decimal originalAmount) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        ContractNumber = contractNumber.Trim();
        Title = title.Trim();
        Counterparty = counterparty.Trim();
        OriginalAmount = originalAmount;
        Status = ContractStatus.Draft;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ContractNumber { get; private set; }
    public string Title { get; private set; }

    /// <summary>The other party: the contractor, supplier or consultant.</summary>
    public string Counterparty { get; private set; }

    public string? Description { get; private set; }
    public DateOnly? SignDate { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>The amount in the signed contract, in rials, before any addendum.</summary>
    public decimal OriginalAmount { get; private set; }

    public ContractStatus Status { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(
        string contractNumber, string title, string counterparty, string? description,
        DateOnly? signDate, DateOnly? startDate, DateOnly? endDate, decimal originalAmount)
    {
        ContractNumber = contractNumber.Trim();
        Title = title.Trim();
        Counterparty = counterparty.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SignDate = signDate;
        StartDate = startDate;
        EndDate = endDate;
        OriginalAmount = originalAmount;
    }

    public void ChangeStatus(ContractStatus status) => Status = status;

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}

/// <summary>
/// A signed change to a contract: money (AmountChange, negative for a reduction), time
/// (ExtensionDays), or both. Numbered 1, 2, 3... per contract. It takes effect - on the contract's
/// value and end date - only once approved.
/// </summary>
public sealed class ContractAddendum : AuditableEntity<Guid>
{
    private ContractAddendum() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ContractAddendum(
        Guid id, Guid tenantId, Guid contractId, int number, string title, string? description,
        DateOnly? addendumDate, decimal amountChange, int extensionDays) : base(id)
    {
        TenantId = tenantId;
        ContractId = contractId;
        Number = number;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AddendumDate = addendumDate;
        AmountChange = amountChange;
        ExtensionDays = extensionDays;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ContractId { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public DateOnly? AddendumDate { get; private set; }

    /// <summary>Rials added to (positive) or taken from (negative) the contract's value.</summary>
    public decimal AmountChange { get; private set; }

    /// <summary>Calendar days added to the contract's end date.</summary>
    public int ExtensionDays { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(string title, string? description, DateOnly? addendumDate, decimal amountChange, int extensionDays)
    {
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AddendumDate = addendumDate;
        AmountChange = amountChange;
        ExtensionDays = extensionDays;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}

/// <summary>
/// A bill against a contract. InvoiceNumber is held with plain 0-9 digits (unique within the
/// contract). PaidAmount is what has been paid so far in total; there is no payment history, only
/// the running total and the date of the latest payment.
/// </summary>
public sealed class ContractInvoice : AuditableEntity<Guid>
{
    private ContractInvoice() : base(Guid.Empty)
    {
        InvoiceNumber = string.Empty;
    }

    public ContractInvoice(
        Guid id, Guid tenantId, Guid contractId, string invoiceNumber, DateOnly invoiceDate, decimal amount, string? description) : base(id)
    {
        TenantId = tenantId;
        ContractId = contractId;
        InvoiceNumber = invoiceNumber.Trim();
        InvoiceDate = invoiceDate;
        Amount = amount;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ContractId { get; private set; }
    public string InvoiceNumber { get; private set; }
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>The invoiced amount in rials.</summary>
    public decimal Amount { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Total paid against this invoice so far, in rials (0 to Amount).</summary>
    public decimal PaidAmount { get; private set; }

    public DateOnly? LastPaymentDate { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(string invoiceNumber, DateOnly invoiceDate, decimal amount, string? description)
    {
        InvoiceNumber = invoiceNumber.Trim();
        InvoiceDate = invoiceDate;
        Amount = amount;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public void RecordPayment(decimal paidAmount, DateOnly? paymentDate)
    {
        PaidAmount = paidAmount;
        LastPaymentDate = paidAmount > 0 ? paymentDate : null;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
