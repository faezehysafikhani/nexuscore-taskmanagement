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
    /// Which posts grant each permission to this user, keyed by permission name (a readable label per post,
    /// for the access page to explain a grant that is not changed on the user).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetGrantingPositionsAsync(Guid userId, CancellationToken cancellationToken);
}
