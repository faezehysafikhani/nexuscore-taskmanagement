using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class SprintRepository(AgileDbContext dbContext) : ISprintRepository
{
    public Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Sprints.SingleOrDefaultAsync(sprint => sprint.Id == id, cancellationToken);

    public Task<Sprint?> GetByNumberAsync(Guid projectId, int number, CancellationToken cancellationToken) =>
        dbContext.Sprints.SingleOrDefaultAsync(sprint => sprint.ProjectId == projectId && sprint.Number == number, cancellationToken);

    public async Task<IReadOnlyList<Sprint>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Sprints.Where(sprint => sprint.ProjectId == projectId).OrderBy(sprint => sprint.Number).ToListAsync(cancellationToken);

    public async Task AddAsync(Sprint sprint, CancellationToken cancellationToken)
    {
        await dbContext.Sprints.AddAsync(sprint, cancellationToken);
    }

    public Task RemoveAsync(Sprint sprint, CancellationToken cancellationToken)
    {
        dbContext.Sprints.Remove(sprint);
        return Task.CompletedTask;
    }
}

public sealed class SprintEventRepository(AgileDbContext dbContext) : ISprintEventRepository
{
    public async Task<IReadOnlyList<SprintEvent>> ListBySprintAsync(Guid projectId, int sprintNumber, CancellationToken cancellationToken) =>
        await dbContext.SprintEvents
            .Where(e => e.ProjectId == projectId && e.SprintNumber == sprintNumber)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SprintEvent>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.SprintEvents.Where(e => e.ProjectId == projectId).OrderBy(e => e.OccurredAtUtc).ToListAsync(cancellationToken);

    public async Task AddAsync(SprintEvent sprintEvent, CancellationToken cancellationToken)
    {
        await dbContext.SprintEvents.AddAsync(sprintEvent, cancellationToken);
    }

    public Task RemoveRangeAsync(IReadOnlyCollection<SprintEvent> events, CancellationToken cancellationToken)
    {
        dbContext.SprintEvents.RemoveRange(events);
        return Task.CompletedTask;
    }
}

public sealed class AgileChecklistRepository(AgileDbContext dbContext) : IAgileChecklistRepository
{
    public Task<AgileChecklistItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChecklistItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AgileChecklistItem>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await dbContext.ChecklistItems.Where(item => item.TaskId == taskId).OrderBy(item => item.Order).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgileChecklistItem>> ListByTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        taskIds.Count == 0 ? [] : await dbContext.ChecklistItems.Where(item => taskIds.Contains(item.TaskId)).ToListAsync(cancellationToken);

    public async Task AddAsync(AgileChecklistItem item, CancellationToken cancellationToken)
    {
        await dbContext.ChecklistItems.AddAsync(item, cancellationToken);
    }

    public Task RemoveAsync(AgileChecklistItem item, CancellationToken cancellationToken)
    {
        dbContext.ChecklistItems.Remove(item);
        return Task.CompletedTask;
    }

    public Task RemoveRangeAsync(IReadOnlyCollection<AgileChecklistItem> items, CancellationToken cancellationToken)
    {
        dbContext.ChecklistItems.RemoveRange(items);
        return Task.CompletedTask;
    }
}
