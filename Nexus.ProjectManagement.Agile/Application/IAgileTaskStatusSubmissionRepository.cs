using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileTaskStatusSubmissionRepository
{
    Task<AgileTaskStatusSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken);
    Task AddAsync(AgileTaskStatusSubmission submission, CancellationToken cancellationToken);
}
