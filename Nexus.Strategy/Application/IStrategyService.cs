using Nexus.StrategyManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.StrategyManagement.Application;

public interface IStrategyService
{
    Task<Result<IReadOnlyList<StrategyDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<StrategyDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<StrategyDto>> CreateAsync(CreateStrategyRequest request, CancellationToken cancellationToken);
    Task<Result<StrategyDto>> UpdateAsync(Guid id, UpdateStrategyRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
