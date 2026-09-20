using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskRepository
{
    Task<Risk?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Risk>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(Risk risk, CancellationToken cancellationToken);
}
