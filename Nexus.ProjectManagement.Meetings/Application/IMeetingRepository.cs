using Nexus.ProjectManagement.Meetings.Domain;

namespace Nexus.ProjectManagement.Meetings.Application;

public interface IMeetingRepository
{
    /// <summary>The meeting with its participants.</summary>
    Task<Meeting?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Meeting>> ListByProjectAsync(Guid projectId, MeetingStatus? status, CancellationToken cancellationToken);
    Task<IReadOnlyList<Meeting>> ListForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);
    Task AddAsync(Meeting meeting, CancellationToken cancellationToken);
    Task RemoveAsync(Meeting meeting, CancellationToken cancellationToken);
}
