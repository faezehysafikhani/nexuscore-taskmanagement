using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ScheduleBaselineRepository(WaterfallDbContext dbContext) : IScheduleBaselineRepository
{
    public Task<ScheduleBaseline?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ScheduleBaselines.SingleOrDefaultAsync(baseline => baseline.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScheduleBaseline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ScheduleBaselines
            .Where(baseline => baseline.ProjectId == projectId)
            .OrderBy(baseline => baseline.Number)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleBaselineActivity>> ListActivitiesAsync(IReadOnlyCollection<Guid> baselineIds, CancellationToken cancellationToken) =>
        baselineIds.Count == 0
            ? []
            : await dbContext.ScheduleBaselineActivities
                .Where(activity => baselineIds.Contains(activity.BaselineId))
                .ToListAsync(cancellationToken);

    public async Task AddAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken cancellationToken)
    {
        await dbContext.ScheduleBaselines.AddAsync(baseline, cancellationToken);
        await dbContext.ScheduleBaselineActivities.AddRangeAsync(activities, cancellationToken);
    }

    public Task RemoveAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken cancellationToken)
    {
        dbContext.ScheduleBaselineActivities.RemoveRange(activities);
        dbContext.ScheduleBaselines.Remove(baseline);
        return Task.CompletedTask;
    }
}
