using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileTaskStatusSubmissionRepository(AgileDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IAgileTaskStatusSubmissionRepository
{
    public Task<AgileTaskStatusSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.StatusSubmissions.SingleOrDefaultAsync(submission => submission.Id == id && (tenant == null || submission.TenantId == tenant), cancellationToken);
    }

    public async Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.StatusSubmissions
            .Where(submission => projectIds.Contains(submission.ProjectId))
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await dbContext.StatusSubmissions
            .Where(submission => submission.TaskId == taskId)
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AgileTaskStatusSubmission submission, CancellationToken cancellationToken) =>
        await dbContext.StatusSubmissions.AddAsync(submission, cancellationToken);
}
