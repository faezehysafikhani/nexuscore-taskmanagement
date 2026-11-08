using Nexus.Actions.Domain;

namespace Nexus.Actions.Application;

public interface IActionItemRepository
{
    Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);
    Task AddAsync(ActionItem action, CancellationToken cancellationToken);
    /// <summary>A default member, so another implementation (a test double, say) keeps compiling.</summary>
    Task RemoveAsync(ActionItem action, CancellationToken cancellationToken) => throw new NotSupportedException();
}
