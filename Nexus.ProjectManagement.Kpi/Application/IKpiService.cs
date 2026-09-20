using Nexus.ProjectManagement.Kpi.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Kpi.Application;

public interface IKpiService
{
    Task<Result<IReadOnlyList<KpiDefinitionDto>>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken cancellationToken);
    Task<Result<KpiDefinitionDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<KpiDefinitionDto>> CreateAsync(CreateKpiDefinitionRequest request, CancellationToken cancellationToken);
    Task<Result<KpiDefinitionDto>> UpdateAsync(Guid id, UpdateKpiDefinitionRequest request, CancellationToken cancellationToken);
}
