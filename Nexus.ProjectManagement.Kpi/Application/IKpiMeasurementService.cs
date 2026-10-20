using Nexus.ProjectManagement.Kpi.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Kpi.Application;

public interface IKpiMeasurementService
{
    Task<Result<IReadOnlyList<KpiMeasurementDto>>> ListAsync(Guid kpiDefinitionId, CancellationToken cancellationToken);
    Task<Result<KpiMeasurementDto>> RecordAsync(Guid kpiDefinitionId, RecordKpiMeasurementRequest request, CancellationToken cancellationToken);
    Task<Result<KpiMeasurementDto>> UpdateAsync(Guid kpiDefinitionId, Guid id, UpdateKpiMeasurementRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid kpiDefinitionId, Guid id, CancellationToken cancellationToken);
}
