using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>What the records say about a period: milestones and obligations due in it, and how many of them were late or missed.</summary>
public sealed record EvaluationMetricsDto(int MilestonesInPeriod, int LateMilestones, int ObligationsInPeriod, int MissedObligations);

public sealed record SupplierEvaluationDto(
    Guid Id, Guid SupplierId, Guid? ContractId, DateOnly PeriodStart, string PeriodStartFa, DateOnly PeriodEnd, string PeriodEndFa,
    int QualityScore, int TimelinessScore, int ComplianceScore, int CooperationScore, decimal OverallScore,
    EvaluationMetricsDto Metrics, string? Notes, DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId);

/// <summary>Scores are 1 to 5. ContractId narrows the evaluation (and its metrics) to one contract of the supplier.</summary>
public sealed record SaveSupplierEvaluationRequest(
    DateOnly PeriodStart, DateOnly PeriodEnd, int QualityScore, int TimelinessScore, int ComplianceScore, int CooperationScore,
    Guid? ContractId = null, string? Notes = null);

public interface ISupplierEvaluationService
{
    Task<Result<IReadOnlyList<SupplierEvaluationDto>>> ListAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken);

    /// <summary>The metrics an evaluation of this period would record, for the evaluator to see before scoring.</summary>
    Task<Result<EvaluationMetricsDto>> GetMetricsAsync(Guid tenantId, Guid supplierId, DateOnly from, DateOnly to, Guid? contractId, CancellationToken cancellationToken);

    Task<Result<SupplierEvaluationDto>> CreateAsync(Guid tenantId, Guid supplierId, SaveSupplierEvaluationRequest request, CancellationToken cancellationToken);
    Task<Result<SupplierEvaluationDto>> UpdateAsync(Guid tenantId, Guid supplierId, Guid id, SaveSupplierEvaluationRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid tenantId, Guid supplierId, Guid id, CancellationToken cancellationToken);
}

public sealed class SupplierEvaluationService(
    ISupplierRepository suppliers,
    ICommitmentRepository commitments,
    IContractsUnitOfWork unitOfWork,
    TimeProvider? clock = null) : ISupplierEvaluationService
{
    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<Result<IReadOnlyList<SupplierEvaluationDto>>> ListAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken)
    {
        if (await FindSupplierAsync(tenantId, supplierId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<SupplierEvaluationDto>>(Error.NotFound("Supplier not found."));
        }

        var items = await suppliers.ListEvaluationsAsync(supplierId, cancellationToken);
        return Result.Success<IReadOnlyList<SupplierEvaluationDto>>(items
            .OrderByDescending(e => e.PeriodEnd).ThenByDescending(e => e.CreatedAtUtc).Select(ToDto).ToList());
    }

    public async Task<Result<EvaluationMetricsDto>> GetMetricsAsync(
        Guid tenantId, Guid supplierId, DateOnly from, DateOnly to, Guid? contractId, CancellationToken cancellationToken)
    {
        if (await FindSupplierAsync(tenantId, supplierId, cancellationToken) is null)
        {
            return Result.Failure<EvaluationMetricsDto>(Error.NotFound("Supplier not found."));
        }

        var error = await ValidateScopeAsync(tenantId, supplierId, from, to, contractId, cancellationToken);
        return error is not null
            ? Result.Failure<EvaluationMetricsDto>(error)
            : Result.Success(await MeasureAsync(tenantId, supplierId, from, to, contractId, cancellationToken));
    }

    public async Task<Result<SupplierEvaluationDto>> CreateAsync(Guid tenantId, Guid supplierId, SaveSupplierEvaluationRequest request, CancellationToken cancellationToken)
    {
        if (await FindSupplierAsync(tenantId, supplierId, cancellationToken) is null)
        {
            return Result.Failure<SupplierEvaluationDto>(Error.NotFound("Supplier not found."));
        }

        var error = ValidateScores(request) ?? await ValidateScopeAsync(tenantId, supplierId, request.PeriodStart, request.PeriodEnd, request.ContractId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<SupplierEvaluationDto>(error);
        }

        var evaluation = new SupplierEvaluation(Guid.NewGuid(), tenantId, supplierId);
        await ApplyAsync(evaluation, tenantId, supplierId, request, cancellationToken);
        await suppliers.AddEvaluationAsync(evaluation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(evaluation));
    }

    public async Task<Result<SupplierEvaluationDto>> UpdateAsync(
        Guid tenantId, Guid supplierId, Guid id, SaveSupplierEvaluationRequest request, CancellationToken cancellationToken)
    {
        var evaluation = await suppliers.GetEvaluationAsync(id, cancellationToken);
        if (evaluation is null || evaluation.SupplierId != supplierId || evaluation.TenantId != tenantId)
        {
            return Result.Failure<SupplierEvaluationDto>(Error.NotFound("Evaluation not found."));
        }

        var error = ValidateScores(request) ?? await ValidateScopeAsync(tenantId, supplierId, request.PeriodStart, request.PeriodEnd, request.ContractId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<SupplierEvaluationDto>(error);
        }

        // The facts are taken again: the period, the contract, or the records themselves may have changed since.
        await ApplyAsync(evaluation, tenantId, supplierId, request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(evaluation));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid supplierId, Guid id, CancellationToken cancellationToken)
    {
        var evaluation = await suppliers.GetEvaluationAsync(id, cancellationToken);
        if (evaluation is null || evaluation.SupplierId != supplierId || evaluation.TenantId != tenantId)
        {
            return Result.Failure(Error.NotFound("Evaluation not found."));
        }

        await suppliers.RemoveEvaluationAsync(evaluation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task ApplyAsync(SupplierEvaluation evaluation, Guid tenantId, Guid supplierId, SaveSupplierEvaluationRequest request, CancellationToken cancellationToken)
    {
        evaluation.Update(
            request.ContractId, request.PeriodStart, request.PeriodEnd, request.QualityScore, request.TimelinessScore,
            request.ComplianceScore, request.CooperationScore, request.Notes);
        var m = await MeasureAsync(tenantId, supplierId, request.PeriodStart, request.PeriodEnd, request.ContractId, cancellationToken);
        evaluation.SetMetrics(m.MilestonesInPeriod, m.LateMilestones, m.ObligationsInPeriod, m.MissedObligations);
    }

    /// <summary>
    /// Milestones and dated obligations of the supplier's contracts (or the one contract) that fell due in the period. A milestone is late when it
    /// was completed after its due date, or is still not completed past it; an obligation is missed when it was breached, or is still open past
    /// its due date.
    /// </summary>
    private async Task<EvaluationMetricsDto> MeasureAsync(Guid tenantId, Guid supplierId, DateOnly from, DateOnly to, Guid? contractId, CancellationToken cancellationToken)
    {
        var today = Today;
        var contracts = (await suppliers.ListContractsAsync(tenantId, supplierId, cancellationToken))
            .Where(c => contractId is null || c.Id == contractId)
            .ToList();

        int milestones = 0, late = 0, obligations = 0, missed = 0;
        foreach (var contract in contracts)
        {
            foreach (var m in (await commitments.ListMilestonesAsync(contract.Id, cancellationToken)).Where(m => m.DueDate >= from && m.DueDate <= to))
            {
                milestones++;
                if (m.Status == ContractMilestoneStatus.Completed ? m.CompletedDate > m.DueDate : m.DueDate < today)
                {
                    late++;
                }
            }

            foreach (var o in (await commitments.ListObligationsAsync(contract.Id, cancellationToken)).Where(o => o.DueDate >= from && o.DueDate <= to))
            {
                obligations++;
                if (o.Status == ObligationStatus.Breached || o.IsOverdue(today))
                {
                    missed++;
                }
            }
        }

        return new EvaluationMetricsDto(milestones, late, obligations, missed);
    }

    private async Task<Supplier?> FindSupplierAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetAsync(supplierId, cancellationToken);
        return supplier is not null && supplier.TenantId == tenantId ? supplier : null;
    }

    private async Task<Error?> ValidateScopeAsync(Guid tenantId, Guid supplierId, DateOnly from, DateOnly to, Guid? contractId, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return Error.Validation("The end of the period cannot be before its start.");
        }

        if (contractId is { } id && !(await suppliers.ListContractsAsync(tenantId, supplierId, cancellationToken)).Any(c => c.Id == id))
        {
            return Error.Validation("The contract is not one of this supplier's.");
        }

        return null;
    }

    private static Error? ValidateScores(SaveSupplierEvaluationRequest request)
    {
        int[] scores = [request.QualityScore, request.TimelinessScore, request.ComplianceScore, request.CooperationScore];
        if (scores.Any(s => s is < SupplierEvaluation.MinScore or > SupplierEvaluation.MaxScore))
        {
            return Error.Validation($"Every score is from {SupplierEvaluation.MinScore} to {SupplierEvaluation.MaxScore}.");
        }

        return request.Notes is { Length: > 2000 } ? Error.Validation("The notes are at most 2000 characters.") : null;
    }

    private static SupplierEvaluationDto ToDto(SupplierEvaluation e) => new(
        e.Id, e.SupplierId, e.ContractId, e.PeriodStart, PersianDates.Format(e.PeriodStart)!, e.PeriodEnd, PersianDates.Format(e.PeriodEnd)!,
        e.QualityScore, e.TimelinessScore, e.ComplianceScore, e.CooperationScore, e.OverallScore,
        new EvaluationMetricsDto(e.MilestonesInPeriod, e.LateMilestones, e.ObligationsInPeriod, e.MissedObligations),
        e.Notes, e.CreatedAtUtc, e.CreatedByUserId);
}
