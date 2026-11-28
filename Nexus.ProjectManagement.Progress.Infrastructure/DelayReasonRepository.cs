using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public sealed class DelayReasonRepository(ProgressDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IDelayReasonRepository
{
    public Task<DelayReason?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.DelayReasons.SingleOrDefaultAsync(reason => reason.Id == id && (tenant == null || reason.TenantId == tenant), cancellationToken);
    }

    public async Task<IReadOnlyList<DelayReason>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.DelayReasons
            .Where(reason => projectIds.Contains(reason.ProjectId))
            .OrderByDescending(reason => reason.RegisterDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DelayReason>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.DelayReasons
            .Where(reason => reason.ProjectId == projectId)
            .OrderByDescending(reason => reason.RegisterDate)
            .ThenByDescending(reason => reason.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DelayReason delayReason, CancellationToken cancellationToken)
    {
        await dbContext.DelayReasons.AddAsync(delayReason, cancellationToken);
    }

    public Task RemoveAsync(DelayReason delayReason, CancellationToken cancellationToken)
    {
        dbContext.DelayReasons.Remove(delayReason);
        return Task.CompletedTask;
    }
}
