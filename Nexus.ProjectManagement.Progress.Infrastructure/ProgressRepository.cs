using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public sealed class ProgressRepository(ProgressDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IProgressRepository
{
    public Task<ProgressUpdate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.ProgressUpdates.SingleOrDefaultAsync(update => update.Id == id && (tenant == null || update.TenantId == tenant), cancellationToken);
    }

    public async Task<IReadOnlyList<ProgressUpdate>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ProgressUpdates
            .Where(update => projectIds.Contains(update.ProjectId))
            .OrderByDescending(update => update.RegisterDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProgressUpdate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProgressUpdates
            .Where(update => update.ProjectId == projectId)
            .OrderByDescending(update => update.RegisterDate)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken)
    {
        await dbContext.ProgressUpdates.AddAsync(progressUpdate, cancellationToken);
    }

    public Task RemoveAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken)
    {
        dbContext.ProgressUpdates.Remove(progressUpdate);
        return Task.CompletedTask;
    }
}
