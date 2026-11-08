using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskSettingsRepository
{
    Task<RiskProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RiskProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);
    Task AddAsync(RiskProjectSettings settings, CancellationToken cancellationToken);
}
