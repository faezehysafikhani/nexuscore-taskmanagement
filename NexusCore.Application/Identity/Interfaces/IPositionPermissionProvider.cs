namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Contributes the permissions a user gets from the posts they hold (their سِمَت and the post itself),
/// and from the posts they stand in for while a stand-in (جانشین) is on.
///
/// The seam through which the organization structure affects identity, shaped like
/// <see cref="IUserGroupPermissionProvider"/>: a host without the structure registers nothing and the
/// null implementation contributes nothing, so permission resolution is exactly what it was before.
/// Permissions are named (not by id): the structure keeps its own tables and the permission name is
/// the stable identifier.
/// </summary>
public interface IPositionPermissionProvider
{
    /// <summary>Permission names the user gets now from the posts they hold or stand in for.</summary>
    Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="GetPermissionNamesAsync(Guid, CancellationToken)"/>, but only from these posts (the ones the
    /// person chose to work as; those not held now are ignored, so this can only narrow). Null, or none of them
    /// held, means all the posts, as before.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, IReadOnlyCollection<Guid>? onlyPosts, CancellationToken cancellationToken) =>
        GetPermissionNamesAsync(userId, cancellationToken);

    /// <summary>
    /// Which posts grant each permission to this user, keyed by permission name (a readable label per post,
    /// for the access page to explain a grant that is not changed on the user).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetGrantingPositionsAsync(Guid userId, CancellationToken cancellationToken);
}
