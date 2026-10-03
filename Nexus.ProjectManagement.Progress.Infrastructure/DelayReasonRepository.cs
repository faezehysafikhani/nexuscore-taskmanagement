using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public sealed class DelayReasonRepository(ProgressDbContext dbContext) : IDelayReasonRepository
{
    public Task<DelayReason?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.DelayReasons.SingleOrDefaultAsync(reason => reason.Id == id, cancellationToken);

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
