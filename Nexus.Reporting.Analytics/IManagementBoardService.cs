using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Analytics;

public interface IManagementBoardService
{
    Task<Result<HealthBoardDto>> GetHealthBoardAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<AttentionBoardDto>> GetAttentionBoardAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<RiskOverviewDto>> GetRiskOverviewAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
    Task<Result<KpiOverviewDto>> GetKpiOverviewAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken);
}
