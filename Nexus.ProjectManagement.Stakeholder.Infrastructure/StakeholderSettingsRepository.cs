using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

public sealed class StakeholderSettingsRepository(StakeholderManagementDbContext dbContext) : IStakeholderSettingsRepository
{
    public Task<StakeholderProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
        dbContext.ProjectSettings.SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<StakeholderProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.Where(settings => projectIds.Contains(settings.ProjectId)).ToListAsync(cancellationToken);

    public async Task AddAsync(StakeholderProjectSettings settings, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.AddAsync(settings, cancellationToken);
}
