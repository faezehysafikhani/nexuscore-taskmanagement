using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public sealed class ProgressRepository(ProgressDbContext dbContext) : IProgressRepository
{
    public Task<ProgressUpdate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProgressUpdates.SingleOrDefaultAsync(update => update.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProgressUpdate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProgressUpdates
            .Where(update => update.ProjectId == projectId)
            .OrderByDescending(update => update.RegisterDate)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ProgressUpdate progressUpdate, CancellationToken cancellationToken)
    {
        await dbContext.ProgressUpdates.AddAsync(progressUpdate, cancellationToken);
    }
}
