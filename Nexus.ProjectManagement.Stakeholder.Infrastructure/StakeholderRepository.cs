using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

public sealed class StakeholderRepository(StakeholderManagementDbContext dbContext) : IStakeholderRepository
{
    public Task<Stakeholder?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Stakeholders.SingleOrDefaultAsync(stakeholder => stakeholder.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Stakeholder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Stakeholders.Where(stakeholder => stakeholder.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task AddAsync(Stakeholder stakeholder, CancellationToken cancellationToken)
    {
        await dbContext.Stakeholders.AddAsync(stakeholder, cancellationToken);
    }
}
