using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>Entities to DTOs, with every amount and date also written the Persian way.</summary>
public static class ContractMapper
{
    /// <summary>The largest amount accepted anywhere; it is also the largest that can be written in words.</summary>
    public const decimal MaxAmount = PersianNumbers.MaxValue;

    public static MoneyDto Money(decimal rials)
    {
        // Sums of valid amounts stay in range, but a figure that cannot be written in words
        // must not break the whole response.
        var inRange = Math.Abs(Math.Round(rials, 0, MidpointRounding.AwayFromZero)) <= PersianNumbers.MaxValue;
        return new MoneyDto(
            rials,
            inRange ? PersianNumbers.RialsInWords(rials) : string.Empty,
            PersianNumbers.FormatRials(rials),
            PersianNumbers.FormatTomans(rials),
            inRange ? PersianNumbers.TomansInWords(rials) : string.Empty);
    }

    public static ContractSummaryDto ToDto(ContractSummary s) => new(
        Money(s.CurrentAmount), s.CurrentEndDate, PersianDates.Format(s.CurrentEndDate),
        s.ApprovedAddendaAmount, s.ApprovedExtensionDays, s.PendingAddendaAmount,
        Money(s.InvoicedAmount), s.ReservedAmount, Money(s.PaidAmount),
        Money(s.RemainingCommitment), Money(s.OutstandingPayable),
        s.InvoicedPercent, s.PaidPercent, s.IsOverInvoiced);

    public static ContractDto ToDto(Contract c, ContractSummary summary) => new(
        c.Id, c.TenantId, c.ProjectId, c.ContractNumber, PersianNumbers.ToPersianDigits(c.ContractNumber), c.Title, c.Counterparty,
        c.Description, c.SignDate, PersianDates.Format(c.SignDate), c.StartDate, PersianDates.Format(c.StartDate),
        c.EndDate, PersianDates.Format(c.EndDate), Money(c.OriginalAmount), c.Status, c.ApprovalStatus,
        ToDto(summary), c.CreatedByUserId);

    public static ContractAddendumDto ToDto(ContractAddendum a) => new(
        a.Id, a.ContractId, a.Number, a.Title, a.Description, a.AddendumDate, PersianDates.Format(a.AddendumDate),
        a.AmountChange, PersianNumbers.FormatRials(a.AmountChange),
        Math.Abs(a.AmountChange) <= PersianNumbers.MaxValue ? PersianNumbers.RialsInWords(a.AmountChange) : string.Empty,
        a.ExtensionDays, a.ApprovalStatus, a.CreatedByUserId);

    public static ContractInvoiceDto ToDto(ContractInvoice i) => new(
        i.Id, i.ContractId, i.InvoiceNumber, PersianNumbers.ToPersianDigits(i.InvoiceNumber), i.InvoiceDate, PersianDates.Format(i.InvoiceDate),
        Money(i.Amount), i.Description, Money(i.PaidAmount), Money(i.Amount - i.PaidAmount),
        i.LastPaymentDate, PersianDates.Format(i.LastPaymentDate), i.ApprovalStatus, i.CreatedByUserId);

    /// <summary>A number typed in any script, reduced to plain 0-9 digits and trimmed, so it can be compared and kept unique.</summary>
    public static string NormalizeNumber(string? number) => PersianNumbers.ToLatinDigits(number).Trim();
}
