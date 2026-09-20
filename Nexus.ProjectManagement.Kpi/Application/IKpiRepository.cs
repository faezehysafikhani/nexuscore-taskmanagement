using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Application;

public interface IKpiRepository
{
    Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken cancellationToken);
    Task AddAsync(KpiDefinition kpi, CancellationToken cancellationToken);
}
