using System.Text.Json.Serialization;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>Why a supplier is flagged for attention. Sent as its name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SupplierAttention>))]
public enum SupplierAttention
{
    /// <summary>The latest evaluation's overall score is below <see cref="SupplierDashboardService.LowScore"/>.</summary>
    LowScore,

    /// <summary>One or more milestones are past their due date and not completed.</summary>
    OverdueMilestones,

    /// <summary>One or more obligations were breached, or are open past their due date.</summary>
    MissedObligations,

    /// <summary>The supplier has an active contract but has never been evaluated.</summary>
    NotEvaluated
}

/// <summary>One supplier across every project of the tenant. Money counts approved contracts only.</summary>
public sealed record SupplierDashboardRowDto(
    Guid SupplierId, string Name, SupplierCategory Category, SupplierStatus Status,
    int ContractCount, int ActiveContractCount, int ProjectCount,
    MoneyDto ContractAmount, MoneyDto InvoicedAmount, MoneyDto OutstandingPayable,
    int OverdueMilestones, int MissedObligations,
    int EvaluationCount, decimal? LatestScore, decimal? AverageScore, string? LatestPeriodEndFa,
    IReadOnlyList<SupplierAttention> Attention);

public sealed record SupplierDashboardTotalsDto(
    int SupplierCount, int ActiveSupplierCount, int ContractCount, int ProjectCount,
    MoneyDto ContractAmount, MoneyDto InvoicedAmount, MoneyDto OutstandingPayable,
    int OverdueMilestones, int MissedObligations, int EvaluatedSuppliers, decimal? AverageLatestScore, int NeedingAttention);

/// <summary>Rows are ordered by contract amount, largest first.</summary>
public sealed record SupplierDashboardDto(SupplierDashboardTotalsDto Totals, IReadOnlyList<SupplierDashboardRowDto> Rows);

public interface ISupplierDashboardService
{
    Task<Result<SupplierDashboardDto>> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// The suppliers side by side for senior management: what each holds across all projects, what is late, and how it was last scored -
/// with the reasons a supplier needs attention. Read-only; everything is computed from contracts, commitments and evaluations.
/// </summary>
public sealed class SupplierDashboardService(
    ISupplierRepository suppliers,
    IContractRepository contracts,
    ICommitmentRepository commitments,
    TimeProvider? clock = null) : ISupplierDashboardService
{
    public const decimal LowScore = 2.5m;

    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<Result<SupplierDashboardDto>> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var today = Today;
        var all = await suppliers.ListAsync(tenantId, cancellationToken);
        var tenantContracts = await suppliers.ListContractsAsync(tenantId, null, cancellationToken);
        var ids = tenantContracts.Select(c => c.Id).ToList();
        var addenda = (await contracts.ListAddendaAsync(ids, cancellationToken)).ToLookup(a => a.ContractId);
        var invoices = (await contracts.ListInvoicesAsync(ids, cancellationToken)).ToLookup(i => i.ContractId);
        var milestones = (await commitments.ListMilestonesForContractsAsync(ids, cancellationToken)).ToLookup(m => m.ContractId);
        var obligations = (await commitments.ListObligationsForContractsAsync(ids, cancellationToken)).ToLookup(o => o.ContractId);
        var evaluations = (await suppliers.ListTenantEvaluationsAsync(tenantId, cancellationToken)).ToLookup(e => e.SupplierId);
        var bySupplier = tenantContracts.Where(c => c.SupplierId is not null).ToLookup(c => c.SupplierId!.Value);

        var rows = new List<SupplierDashboardRowDto>();
        foreach (var supplier in all)
        {
            var own = bySupplier[supplier.Id].ToList();
            var approved = own
                .Where(c => c.ApprovalStatus == ApprovalStatus.Approved)
                .Select(c => ContractCalculator.Summarize(c, addenda[c.Id].ToList(), invoices[c.Id].ToList()))
                .ToList();
            var overdue = own.Sum(c => milestones[c.Id].Count(m => m.IsOverdue(today)));
            var missed = own.Sum(c => obligations[c.Id].Count(o => o.Status == ObligationStatus.Breached || o.IsOverdue(today)));
            var evals = evaluations[supplier.Id].ToList();
            var latest = evals.OrderByDescending(e => e.PeriodEnd).ThenByDescending(e => e.CreatedAtUtc).FirstOrDefault();
            var active = own.Count(c => c.Status == ContractStatus.Active);

            var attention = new List<SupplierAttention>();
            if (latest is not null && latest.OverallScore < LowScore) attention.Add(SupplierAttention.LowScore);
            if (overdue > 0) attention.Add(SupplierAttention.OverdueMilestones);
            if (missed > 0) attention.Add(SupplierAttention.MissedObligations);
            if (latest is null && active > 0) attention.Add(SupplierAttention.NotEvaluated);

            rows.Add(new SupplierDashboardRowDto(
                supplier.Id, supplier.Name, supplier.Category, supplier.Status,
                own.Count, active, own.Select(c => c.ProjectId).Distinct().Count(),
                ContractMapper.Money(approved.Sum(s => s.CurrentAmount)),
                ContractMapper.Money(approved.Sum(s => s.InvoicedAmount)),
                ContractMapper.Money(approved.Sum(s => s.OutstandingPayable)),
                overdue, missed,
                evals.Count, latest?.OverallScore,
                evals.Count == 0 ? null : Math.Round(evals.Average(e => e.OverallScore), 2, MidpointRounding.AwayFromZero),
                latest is null ? null : PersianDates.Format(latest.PeriodEnd),
                attention));
        }

        rows = rows.OrderByDescending(r => r.ContractAmount.Rials).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        var scored = rows.Where(r => r.LatestScore is not null).ToList();
        var totals = new SupplierDashboardTotalsDto(
            rows.Count, rows.Count(r => r.Status == SupplierStatus.Active), tenantContracts.Count, tenantContracts.Select(c => c.ProjectId).Distinct().Count(),
            ContractMapper.Money(rows.Sum(r => r.ContractAmount.Rials)),
            ContractMapper.Money(rows.Sum(r => r.InvoicedAmount.Rials)),
            ContractMapper.Money(rows.Sum(r => r.OutstandingPayable.Rials)),
            rows.Sum(r => r.OverdueMilestones), rows.Sum(r => r.MissedObligations), scored.Count,
            scored.Count == 0 ? null : Math.Round(scored.Average(r => r.LatestScore!.Value), 2, MidpointRounding.AwayFromZero),
            rows.Count(r => r.Attention.Count > 0));

        return Result.Success(new SupplierDashboardDto(totals, rows));
    }
}
