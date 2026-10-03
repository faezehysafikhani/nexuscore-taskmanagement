using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IScheduleBaselineRepository
{
    Task<ScheduleBaseline?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScheduleBaseline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The activity rows of the given baselines, as stored.</summary>
    Task<IReadOnlyList<ScheduleBaselineActivity>> ListActivitiesAsync(IReadOnlyCollection<Guid> baselineIds, CancellationToken cancellationToken);

    Task AddAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken cancellationToken);
    Task RemoveAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken cancellationToken);
}
