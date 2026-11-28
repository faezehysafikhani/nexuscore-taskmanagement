using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileTaskRepository(AgileDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IAgileTaskRepository
{
    public Task<AgileTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.AgileTasks.SingleOrDefaultAsync(task => task.Id == id && (tenant == null || task.TenantId == tenant), cancellationToken);
    }

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
