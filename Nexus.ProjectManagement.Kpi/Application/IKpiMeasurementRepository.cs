using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Application;

public interface IKpiMeasurementRepository
{
    Task<KpiMeasurement?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The values of one KPI, oldest first.</summary>
    Task<IReadOnlyList<KpiMeasurement>> ListByKpiAsync(Guid kpiDefinitionId, CancellationToken cancellationToken);

    /// <summary>The values of every KPI of the given projects, oldest first - one read for a whole dashboard.</summary>
    Task<IReadOnlyList<KpiMeasurement>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task AddAsync(KpiMeasurement measurement, CancellationToken cancellationToken);
    Task RemoveAsync(KpiMeasurement measurement, CancellationToken cancellationToken);

    /// <summary>Marks every value of a KPI for removal; the caller saves.</summary>
    Task RemoveByKpiAsync(Guid kpiDefinitionId, CancellationToken cancellationToken);
}
