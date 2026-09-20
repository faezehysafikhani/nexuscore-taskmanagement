using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Application;

public interface IAlignmentRepository
{
    Task<ProjectStrategyAlignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The full Project x Strategy matrix for a tenant, optionally scoped to one
    /// project or one strategy.</summary>
    Task<IReadOnlyList<ProjectStrategyAlignment>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken);

    Task AddAsync(ProjectStrategyAlignment alignment, CancellationToken cancellationToken);
}
