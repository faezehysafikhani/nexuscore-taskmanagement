namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whom a user may start or continue a direct chat with, on top of <see cref="IUserReachScope"/>.
/// Not registered by the Core (then no limit). A product's organization structure can limit it -
/// Nexus.OrgStructure, when OrgStructure:LimitChatToHierarchy is on, keeps it to the user's
/// hierarchy: the people at their level, those below them, their manager and their manager's manager.
/// </summary>
public interface IChatPartnerScope
{
    /// <summary>The people <paramref name="userId"/> may chat with, or null when anyone (within reach) may be.</summary>
    Task<IReadOnlySet<Guid>?> GetPartnersAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
