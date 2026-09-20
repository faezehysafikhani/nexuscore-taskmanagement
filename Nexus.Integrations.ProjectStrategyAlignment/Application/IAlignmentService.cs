using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.StrategyAlignment.Application;

public interface IAlignmentService
{
    Task<Result<IReadOnlyList<ProjectStrategyAlignmentDto>>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken);
    Task<Result<ProjectStrategyAlignmentDto>> CreateAsync(CreateAlignmentRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectStrategyAlignmentDto>> UpdateAsync(Guid id, UpdateAlignmentRequest request, CancellationToken cancellationToken);
}
