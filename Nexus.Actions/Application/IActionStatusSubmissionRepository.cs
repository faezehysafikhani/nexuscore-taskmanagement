using Nexus.Actions.Domain;

namespace Nexus.Actions.Application;

public interface IActionStatusSubmissionRepository
{
    Task<ActionStatusSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ActionStatusSubmission>> ListByActionsAsync(IReadOnlyCollection<Guid> actionIds, CancellationToken cancellationToken);

    /// <summary>Newest first; every request of the tenant's actions of one project.</summary>
    Task<IReadOnlyList<ActionStatusSubmission>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);

    Task AddAsync(ActionStatusSubmission submission, CancellationToken cancellationToken);
}
