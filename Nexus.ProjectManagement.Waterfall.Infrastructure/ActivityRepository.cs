using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ActivityRepository(WaterfallDbContext dbContext) : IActivityRepository
{
    public Task<Activity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Activities.SingleOrDefaultAsync(activity => activity.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Activity>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Activities
            .Where(activity => activity.ProjectId == projectId)
            .OrderBy(activity => activity.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Activity activity, CancellationToken cancellationToken)
    {
        await dbContext.Activities.AddAsync(activity, cancellationToken);
    }

    public Task RemoveAsync(Activity activity, CancellationToken cancellationToken)
    {
        dbContext.Activities.Remove(activity);
        return Task.CompletedTask;
    }
}
