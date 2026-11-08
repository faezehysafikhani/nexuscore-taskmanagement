using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public sealed class WaterfallSettingsRepository(WaterfallDbContext dbContext) : IWaterfallSettingsRepository
{
    public Task<WaterfallProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
        dbContext.ProjectSettings.SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<WaterfallProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.Where(settings => projectIds.Contains(settings.ProjectId)).ToListAsync(cancellationToken);

    public async Task AddAsync(WaterfallProjectSettings settings, CancellationToken cancellationToken) =>
        await dbContext.ProjectSettings.AddAsync(settings, cancellationToken);
}
