using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskRepository(RiskManagementDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IRiskRepository
{
    public Task<Risk?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.Risks.SingleOrDefaultAsync(risk => risk.Id == id && (tenant == null || risk.TenantId == tenant), cancellationToken);
    }

    public async Task<IReadOnlyList<Risk>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Risks
            .Where(risk => risk.ProjectId == projectId)
            .OrderByDescending(risk => risk.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Risk>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.Risks
            .Where(risk => projectIds.Contains(risk.ProjectId))
            .OrderByDescending(risk => risk.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Risk risk, CancellationToken cancellationToken)
    {
        await dbContext.Risks.AddAsync(risk, cancellationToken);
    }

    public Task RemoveAsync(Risk risk, CancellationToken cancellationToken)
    {
        dbContext.Risks.Remove(risk);
        return Task.CompletedTask;
    }
}
