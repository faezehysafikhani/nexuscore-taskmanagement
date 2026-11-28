using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class ActivityProgressSubmissionRepository(WaterfallDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IActivityProgressSubmissionRepository
{
    public Task<ActivityProgressSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.ProgressSubmissions.SingleOrDefaultAsync(submission => submission.Id == id && (tenant == null || submission.TenantId == tenant), cancellationToken);
    }

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
