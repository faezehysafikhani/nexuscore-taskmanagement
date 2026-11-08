using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileTaskRepository(AgileDbContext dbContext) : IAgileTaskRepository
{
    public Task<AgileTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.AgileTasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AgileTask>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken)
    {
        var query = dbContext.AgileTasks.Where(task => task.ProjectId == projectId);

        if (sprintNumber is { } sprint)
        {
            query = query.Where(task => task.SprintNumber == sprint);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgileTask>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0 ? [] : await dbContext.AgileTasks.Where(task => ids.Contains(task.Id)).ToListAsync(cancellationToken);

    public async Task AddAsync(AgileTask task, CancellationToken cancellationToken)
    {
        await dbContext.AgileTasks.AddAsync(task, cancellationToken);
    }

    public Task RemoveAsync(AgileTask task, CancellationToken cancellationToken)
    {
        dbContext.AgileTasks.Remove(task);
        return Task.CompletedTask;
    }
}
