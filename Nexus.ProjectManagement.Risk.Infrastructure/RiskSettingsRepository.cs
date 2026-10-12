using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskSettingsRepository(RiskManagementDbContext dbContext) : IRiskSettingsRepository
{
    public Task<RiskProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
        dbContext.ProjectSettings.SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<RiskProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.Where(settings => projectIds.Contains(settings.ProjectId)).ToListAsync(cancellationToken);

    public async Task AddAsync(RiskProjectSettings settings, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.AddAsync(settings, cancellationToken);
}
