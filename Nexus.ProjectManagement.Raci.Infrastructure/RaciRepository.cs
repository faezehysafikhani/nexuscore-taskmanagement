using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Raci.Application;
using Nexus.ProjectManagement.Raci.Domain;

namespace Nexus.ProjectManagement.Raci.Infrastructure;

public sealed class RaciRepository(RaciDbContext dbContext) : IRaciRepository
{
    public async Task<IReadOnlyList<RaciAssignment>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.RaciAssignments.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RaciAssignment>> ListByItemAsync(Guid projectId, RaciItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        await dbContext.RaciAssignments.Where(a => a.ProjectId == projectId && a.ItemType == itemType && a.ItemId == itemId).ToListAsync(cancellationToken);

    public async Task AddAsync(RaciAssignment assignment, CancellationToken cancellationToken)
    {
        await dbContext.RaciAssignments.AddAsync(assignment, cancellationToken);
    }

    public Task RemoveAsync(RaciAssignment assignment, CancellationToken cancellationToken)
    {
        dbContext.RaciAssignments.Remove(assignment);
        return Task.CompletedTask;
    }
}
