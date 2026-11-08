using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

public interface IStakeholderSettingsRepository
{
    Task<StakeholderProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StakeholderProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);
    Task AddAsync(StakeholderProjectSettings settings, CancellationToken cancellationToken);
}
