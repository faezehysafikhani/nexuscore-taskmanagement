using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Contracts.Application.Dtos;

/// <summary>An amount of rials as it is shown: in words, grouped in Persian digits, and in tomans.</summary>
public sealed record MoneyDto(decimal Rials, string InWords, string Formatted, string TomansFormatted, string TomansInWords);

public sealed record ContractSummaryDto(
    MoneyDto CurrentAmount, DateOnly? CurrentEndDate, string? CurrentEndDateFa,
    decimal ApprovedAddendaAmount, int ApprovedExtensionDays, decimal PendingAddendaAmount,
    MoneyDto InvoicedAmount, decimal ReservedAmount, MoneyDto PaidAmount,
    MoneyDto RemainingCommitment, MoneyDto OutstandingPayable,
    decimal InvoicedPercent, decimal PaidPercent, bool IsOverInvoiced);

/// <summary>Dates are Gregorian; the ...Fa fields are the same dates in the Jalali calendar, in Persian digits.</summary>
public sealed record ContractDto(
    Guid Id, Guid TenantId, Guid ProjectId, string ContractNumber, string ContractNumberFa, string Title, string Counterparty,
    string? Description, DateOnly? SignDate, string? SignDateFa, DateOnly? StartDate, string? StartDateFa,
    DateOnly? EndDate, string? EndDateFa, MoneyDto OriginalAmount, ContractStatus Status, ApprovalStatus ApprovalStatus,
    ContractSummaryDto Summary, Guid? CreatedByUserId);

public sealed record ContractAddendumDto(
    Guid Id, Guid ContractId, int Number, string Title, string? Description, DateOnly? AddendumDate, string? AddendumDateFa,
    decimal AmountChange, string AmountChangeFormatted, string AmountChangeInWords, int ExtensionDays, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId);

public sealed record ContractInvoiceDto(
    Guid Id, Guid ContractId, string InvoiceNumber, string InvoiceNumberFa, DateOnly InvoiceDate, string InvoiceDateFa,
    MoneyDto Amount, string? Description, MoneyDto PaidAmount, MoneyDto UnpaidAmount, DateOnly? LastPaymentDate, string? LastPaymentDateFa,
    ApprovalStatus ApprovalStatus, Guid? CreatedByUserId);

/// <summary>A contract with its addenda and invoices.</summary>
public sealed record ContractDetailDto(
    ContractDto Contract, IReadOnlyList<ContractAddendumDto> Addenda, IReadOnlyList<ContractInvoiceDto> Invoices);

/// <summary>All of a project's contracts added up. Each contract counts only what is approved.</summary>
public sealed record ProjectContractsSummaryDto(
    Guid ProjectId, int ContractCount, MoneyDto TotalContractAmount, MoneyDto TotalInvoiced, MoneyDto TotalPaid,
    MoneyDto TotalRemainingCommitment, MoneyDto TotalOutstandingPayable, decimal InvoicedPercent, decimal PaidPercent,
    int OverInvoicedContracts);

public sealed record CreateContractRequest(
    Guid TenantId, Guid ProjectId, string ContractNumber, string Title, string Counterparty, string? Description,
    DateOnly? SignDate, DateOnly? StartDate, DateOnly? EndDate, decimal OriginalAmount);

public sealed record UpdateContractRequest(
    string ContractNumber, string Title, string Counterparty, string? Description,
    DateOnly? SignDate, DateOnly? StartDate, DateOnly? EndDate, decimal OriginalAmount);

public sealed record ChangeContractStatusRequest(ContractStatus Status);

/// <summary>AmountChange is negative for a reduction; ExtensionDays is how many days the end date moves. At least one must be non-zero.</summary>
public sealed record CreateContractAddendumRequest(
    Guid TenantId, string Title, string? Description, DateOnly? AddendumDate, decimal AmountChange, int ExtensionDays);

public sealed record UpdateContractAddendumRequest(
    string Title, string? Description, DateOnly? AddendumDate, decimal AmountChange, int ExtensionDays);

public sealed record CreateContractInvoiceRequest(
    Guid TenantId, string InvoiceNumber, DateOnly InvoiceDate, decimal Amount, string? Description);

public sealed record UpdateContractInvoiceRequest(string InvoiceNumber, DateOnly InvoiceDate, decimal Amount, string? Description);

/// <summary>The invoice's total paid so far (not an increment), and the date of the latest payment.</summary>
public sealed record RecordInvoicePaymentRequest(decimal PaidAmount, DateOnly? PaymentDate);

public sealed record AmountInWordsDto(MoneyDto Amount);
