using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public sealed class AgileTaskStatusSubmissionRepository(AgileDbContext dbContext) : IAgileTaskStatusSubmissionRepository
{
    public Task<AgileTaskStatusSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StatusSubmissions.SingleOrDefaultAsync(submission => submission.Id == id, cancellationToken);

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
