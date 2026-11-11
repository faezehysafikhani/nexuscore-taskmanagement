namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// The people above someone in the organization chart, nearest first (their manager, that manager's manager, ...).
/// Registered by the organization-structure module when it is installed; a module that wants "the manager of ..." asks
/// for this optionally and carries on without one - it never references the structure module.
/// </summary>
public interface IManagementChainProvider
{
    Task<IReadOnlyList<Guid>> GetChainAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
