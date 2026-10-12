using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IWaterfallSettingsRepository
{
    Task<WaterfallProjectSettings?> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WaterfallProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);
    Task AddAsync(WaterfallProjectSettings settings, CancellationToken cancellationToken);
}
