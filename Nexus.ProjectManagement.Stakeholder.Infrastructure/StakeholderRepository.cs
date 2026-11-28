using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

public sealed class StakeholderRepository(StakeholderManagementDbContext dbContext, NexusCore.SharedKernel.Interfaces.ICurrentUserContext? currentUser = null) : IStakeholderRepository
{
    public Task<Stakeholder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // M7: by id, only within the signed-in user's organization (no user, e.g. a background job: unrestricted).
        var tenant = currentUser?.TenantId;
        return dbContext.Stakeholders.SingleOrDefaultAsync(stakeholder => stakeholder.Id == id && (tenant == null || stakeholder.TenantId == tenant), cancellationToken);
    }

    public async Task<IReadOnlyList<Stakeholder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Stakeholders.Where(stakeholder => stakeholder.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Stakeholder>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.Stakeholders.Where(stakeholder => projectIds.Contains(stakeholder.ProjectId)).ToListAsync(cancellationToken);

    public async Task AddAsync(Stakeholder stakeholder, CancellationToken cancellationToken)
    {
        await dbContext.Stakeholders.AddAsync(stakeholder, cancellationToken);
    }

    public Task RemoveAsync(Stakeholder stakeholder, CancellationToken cancellationToken)
    {
        dbContext.Stakeholders.Remove(stakeholder);
        return Task.CompletedTask;
    }
}
