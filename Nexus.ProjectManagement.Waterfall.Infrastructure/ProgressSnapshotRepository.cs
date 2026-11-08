using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ProgressSnapshotRepository(WaterfallDbContext dbContext) : IProgressSnapshotRepository
{
    public Task<ProgressSnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProgressSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, cancellationToken);

    public Task<ProgressSnapshot?> GetByDateAsync(Guid projectId, DateOnly date, CancellationToken cancellationToken) =>
        dbContext.ProgressSnapshots.SingleOrDefaultAsync(
            snapshot => snapshot.ProjectId == projectId && snapshot.SnapshotDate == date, cancellationToken);

    public async Task<IReadOnlyList<ProgressSnapshot>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProgressSnapshots
            .Where(snapshot => snapshot.ProjectId == projectId)
            .OrderBy(snapshot => snapshot.SnapshotDate)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken)
    {
        await dbContext.ProgressSnapshots.AddAsync(snapshot, cancellationToken);
    }

    public Task RemoveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken)
    {
        dbContext.ProgressSnapshots.Remove(snapshot);
        return Task.CompletedTask;
    }
}
