using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Application.Dtos;

/// <summary>
/// What a supplier's contracts add up to. Only approved contracts count towards the money, as everywhere else. The evaluation figures are
/// the number of periodic evaluations, the overall score of the latest one (by the end of its period) and the average over all of them.
/// </summary>
public sealed record SupplierStatsDto(
    int ContractCount, int ActiveContractCount, MoneyDto ContractAmount, MoneyDto InvoicedAmount,
    MoneyDto PaidAmount, MoneyDto OutstandingPayable,
    int EvaluationCount = 0, decimal? LatestScore = null, string? LatestPeriodEndFa = null, decimal? AverageScore = null);

public sealed record SupplierDto(
    Guid Id, Guid TenantId, string Name, SupplierCategory Category, SupplierStatus Status, string? NationalId,
    string? ContactPerson, string? Phone, string? Email, string? Address, string? Description,
    SupplierStatsDto Stats, Guid? CreatedByUserId);

/// <summary>A supplier with its contracts (across every project), each with its own summary.</summary>
public sealed record SupplierDetailDto(SupplierDto Supplier, IReadOnlyList<ContractDto> Contracts);

public sealed record CreateSupplierRequest(
    string Name, SupplierCategory Category, string? NationalId = null, string? ContactPerson = null,
    string? Phone = null, string? Email = null, string? Address = null, string? Description = null);

public sealed record UpdateSupplierRequest(
    string Name, SupplierCategory Category, SupplierStatus Status, string? NationalId = null, string? ContactPerson = null,
    string? Phone = null, string? Email = null, string? Address = null, string? Description = null);
