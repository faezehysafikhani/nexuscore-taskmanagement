using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Application;

public interface IKpiRepository
{
    Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken cancellationToken);
    /// <summary>The KPIs of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<KpiDefinition>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This KPI repository cannot list the KPIs of several projects.");
    Task AddAsync(KpiDefinition kpi, CancellationToken cancellationToken);
    Task RemoveAsync(KpiDefinition kpi, CancellationToken cancellationToken);
}
