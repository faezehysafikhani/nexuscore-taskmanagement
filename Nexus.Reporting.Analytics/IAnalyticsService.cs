using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Analytics;

public interface IAnalyticsService
{
    Task<Result<ProjectPerformanceDto>> GetProjectPerformanceAsync(Guid tenantId, Guid projectId, DateOnly? asOf, CancellationToken cancellationToken);

    /// <summary>The planned value, earned value and actual cost of one project over time (the cost S-curve).</summary>
    Task<Result<CostCurveDto>> GetProjectCostCurveAsync(Guid tenantId, Guid projectId, int? stepDays, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ProjectPerformanceDto>>> GetProjectsPerformanceAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<UnitPerformanceReportDto>> GetUnitPerformanceAsync(Guid tenantId, int level, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<UnitStatusMatrixDto>> GetUnitStatusMatrixAsync(Guid tenantId, int level, CancellationToken cancellationToken);
    Task<Result<ProjectManagerEvaluationReportDto>> GetProjectManagerEvaluationAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<StrategyAlignmentMatrixDto>> GetStrategyAlignmentMatrixAsync(Guid tenantId, CancellationToken cancellationToken);
}
