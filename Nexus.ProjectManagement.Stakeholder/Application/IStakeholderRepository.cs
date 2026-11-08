using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

public interface IStakeholderRepository
{
    Task<Stakeholder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Stakeholder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>The stakeholders of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<Stakeholder>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This stakeholder repository cannot list the stakeholders of several projects.");

    Task AddAsync(Stakeholder stakeholder, CancellationToken cancellationToken);
    Task RemoveAsync(Stakeholder stakeholder, CancellationToken cancellationToken);
}
