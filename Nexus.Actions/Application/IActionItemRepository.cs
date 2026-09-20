using Nexus.Actions.Domain;

namespace Nexus.Actions.Application;

public interface IActionItemRepository
{
    Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);
    Task AddAsync(ActionItem action, CancellationToken cancellationToken);
}
