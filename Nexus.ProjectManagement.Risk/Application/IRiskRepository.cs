using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskRepository
{
    Task<Risk?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Risk>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>The risks of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<Risk>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This risk repository cannot list the risks of several projects.");

    Task AddAsync(Risk risk, CancellationToken cancellationToken);
    Task RemoveAsync(Risk risk, CancellationToken cancellationToken);
}
