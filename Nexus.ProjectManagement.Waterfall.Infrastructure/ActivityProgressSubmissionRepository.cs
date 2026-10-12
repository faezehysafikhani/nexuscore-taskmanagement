using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ActivityProgressSubmissionRepository(WaterfallDbContext dbContext) : IActivityProgressSubmissionRepository
{
    public Task<ActivityProgressSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProgressSubmissions.SingleOrDefaultAsync(submission => submission.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActivityProgressSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ProgressSubmissions
            .Where(submission => projectIds.Contains(submission.ProjectId))
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActivityProgressSubmission>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken) =>
        await dbContext.ProgressSubmissions
            .Where(submission => submission.ActivityId == activityId)
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ActivityProgressSubmission submission, CancellationToken cancellationToken) =>
        await dbContext.ProgressSubmissions.AddAsync(submission, cancellationToken);
}
