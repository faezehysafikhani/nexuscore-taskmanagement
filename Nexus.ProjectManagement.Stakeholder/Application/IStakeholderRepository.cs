using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

public interface IStakeholderRepository
{
    Task<Stakeholder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Stakeholder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(Stakeholder stakeholder, CancellationToken cancellationToken);
}
