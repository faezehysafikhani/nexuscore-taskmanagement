using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Application.Analytics;

public interface IAnalyticsService
{
    Task<Result<ProjectPerformanceDto>> GetProjectPerformanceAsync(Guid tenantId, Guid projectId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<UnitPerformanceReportDto>> GetUnitPerformanceAsync(Guid tenantId, int level, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<UnitStatusMatrixDto>> GetUnitStatusMatrixAsync(Guid tenantId, int level, CancellationToken cancellationToken);
    Task<Result<ProjectManagerEvaluationReportDto>> GetProjectManagerEvaluationAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<StrategyAlignmentMatrixDto>> GetStrategyAlignmentMatrixAsync(Guid tenantId, CancellationToken cancellationToken);
}
