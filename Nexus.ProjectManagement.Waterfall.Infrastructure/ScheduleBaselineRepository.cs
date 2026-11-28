using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ScheduleBaselineRepository(WaterfallDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IScheduleBaselineRepository
{
    public Task<ScheduleBaseline?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.ScheduleBaselines.SingleOrDefaultAsync(baseline => baseline.Id == id && (tenant == null || baseline.TenantId == tenant), cancellationToken);
    }

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
