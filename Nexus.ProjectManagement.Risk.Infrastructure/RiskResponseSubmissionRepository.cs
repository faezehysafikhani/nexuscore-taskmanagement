using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskResponseSubmissionRepository(RiskManagementDbContext dbContext) : IRiskResponseSubmissionRepository
{
    public Task<RiskResponseSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ResponseSubmissions.SingleOrDefaultAsync(submission => submission.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RiskResponseSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ResponseSubmissions
            .Where(submission => projectIds.Contains(submission.ProjectId))
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RiskResponseSubmission>> ListByRiskAsync(Guid riskId, CancellationToken cancellationToken) =>
        await dbContext.ResponseSubmissions
            .Where(submission => submission.RiskId == riskId)
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RiskResponseSubmission submission, CancellationToken cancellationToken) =>
        await dbContext.ResponseSubmissions.AddAsync(submission, cancellationToken);
}
