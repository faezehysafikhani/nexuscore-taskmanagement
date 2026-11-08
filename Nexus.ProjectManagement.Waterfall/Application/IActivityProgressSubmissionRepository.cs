using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityProgressSubmissionRepository
{
    Task<ActivityProgressSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ActivityProgressSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityProgressSubmission>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken);
    Task AddAsync(ActivityProgressSubmission submission, CancellationToken cancellationToken);
}
