using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ActivityDependencyRepository(WaterfallDbContext dbContext) : IActivityDependencyRepository
{
    public Task<ActivityDependency?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ActivityDependencies.SingleOrDefaultAsync(dependency => dependency.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActivityDependency>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ActivityDependencies
            .Where(dependency => dependency.ProjectId == projectId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActivityDependency>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken) =>
        await dbContext.ActivityDependencies
            .Where(dependency => dependency.PredecessorActivityId == activityId || dependency.SuccessorActivityId == activityId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ActivityDependency dependency, CancellationToken cancellationToken)
    {
        await dbContext.ActivityDependencies.AddAsync(dependency, cancellationToken);
    }

    public Task RemoveAsync(ActivityDependency dependency, CancellationToken cancellationToken)
    {
        dbContext.ActivityDependencies.Remove(dependency);
        return Task.CompletedTask;
    }

    public Task RemoveRangeAsync(IReadOnlyCollection<ActivityDependency> dependencies, CancellationToken cancellationToken)
    {
        dbContext.ActivityDependencies.RemoveRange(dependencies);
        return Task.CompletedTask;
    }
}
