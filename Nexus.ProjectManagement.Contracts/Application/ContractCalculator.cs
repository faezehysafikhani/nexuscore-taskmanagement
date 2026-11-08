using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>
/// A contract's money, worked out. All amounts are rials.
/// <list type="bullet">
/// <item>CurrentAmount - what the contract is worth now: the original amount plus every approved
/// addendum (addenda still waiting for approval, or rejected, do not count).</item>
/// <item>CurrentEndDate - the end date plus the approved addenda's extension days.</item>
/// <item>InvoicedAmount - approved invoices; ReservedAmount - invoices that are not rejected (drafts,
/// pending and approved), which is what new invoices must fit around.</item>
/// <item>PaidAmount - everything paid against invoices.</item>
/// <item>RemainingCommitment - the part of the contract not yet invoiced (CurrentAmount - InvoicedAmount).</item>
/// <item>OutstandingPayable - invoiced but not yet paid (InvoicedAmount - PaidAmount).</item>
/// <item>The two percentages measure invoicing and payment against the current amount.</item>
/// </list>
/// A contract that is not approved is worth nothing yet.
/// </summary>
public static class ContractCalculator
{
    public static ContractSummary Summarize(Contract contract, IEnumerable<ContractAddendum> addenda, IEnumerable<ContractInvoice> invoices)
    {
        var addendaList = addenda.ToList();
        var invoiceList = invoices.ToList();

        var approvedAddenda = addendaList.Where(a => a.ApprovalStatus == ApprovalStatus.Approved).ToList();
        var pendingAddenda = addendaList.Where(a => a.ApprovalStatus is ApprovalStatus.NotSubmitted or ApprovalStatus.PendingApproval).ToList();
        var approved = contract.ApprovalStatus == ApprovalStatus.Approved;

        var currentAmount = approved ? contract.OriginalAmount + approvedAddenda.Sum(a => a.AmountChange) : 0m;
        var invoiced = invoiceList.Where(i => i.ApprovalStatus == ApprovalStatus.Approved).Sum(i => i.Amount);
        var reserved = invoiceList.Where(i => i.ApprovalStatus != ApprovalStatus.Rejected).Sum(i => i.Amount);
        var paid = invoiceList.Sum(i => i.PaidAmount);

        return new ContractSummary(
            currentAmount,
            contract.EndDate?.AddDays(approved ? approvedAddenda.Sum(a => a.ExtensionDays) : 0),
            approvedAddenda.Sum(a => a.AmountChange),
            approvedAddenda.Sum(a => a.ExtensionDays),
            pendingAddenda.Sum(a => a.AmountChange),
            invoiced, reserved, paid,
            currentAmount - invoiced,
            invoiced - paid,
            Percent(invoiced, currentAmount),
            Percent(paid, currentAmount),
            IsOverInvoiced: approved && invoiced > currentAmount);
    }

    /// <summary>What a contract of this value could still be invoiced for, counting every invoice that is not rejected.</summary>
    public static decimal InvoiceableAmount(Contract contract, IEnumerable<ContractAddendum> addenda, IEnumerable<ContractInvoice> invoices)
    {
        var summary = Summarize(contract, addenda, invoices);
        return summary.CurrentAmount - summary.ReservedAmount;
    }

    private static decimal Percent(decimal part, decimal whole) =>
        whole > 0 ? Math.Round(part * 100m / whole, 2) : 0m;
}

public sealed record ContractSummary(
    decimal CurrentAmount, DateOnly? CurrentEndDate,
    decimal ApprovedAddendaAmount, int ApprovedExtensionDays, decimal PendingAddendaAmount,
    decimal InvoicedAmount, decimal ReservedAmount, decimal PaidAmount,
    decimal RemainingCommitment, decimal OutstandingPayable,
    decimal InvoicedPercent, decimal PaidPercent, bool IsOverInvoiced);
