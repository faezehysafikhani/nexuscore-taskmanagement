using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskResponseSubmissionRepository(RiskManagementDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IRiskResponseSubmissionRepository
{
    public Task<RiskResponseSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.ResponseSubmissions.SingleOrDefaultAsync(submission => submission.Id == id && (tenant == null || submission.TenantId == tenant), cancellationToken);
    }

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
