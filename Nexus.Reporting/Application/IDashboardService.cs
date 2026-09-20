using Nexus.Reporting.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Application;

public interface IDashboardService
{
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid tenantId, Guid? organizationUnitId, CancellationToken cancellationToken);
    Task<Result<MyDashboardDto>> GetMyDashboardAsync(Guid tenantId, Guid currentUserId, CancellationToken cancellationToken);
    Task<Result<ProjectDashboardDto>> GetProjectDashboardAsync(Guid projectId, CancellationToken cancellationToken);
}
