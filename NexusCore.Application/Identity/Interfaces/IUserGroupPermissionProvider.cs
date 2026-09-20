namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Contributes the permissions a user inherits from the groups they belong to.
///
/// This is the ONLY seam through which the user-group feature affects the rest of identity.
/// When the feature is disabled a null implementation is registered, both methods return
/// empty, and everything behaves exactly as it did before groups existed.
/// </summary>
public interface IUserGroupPermissionProvider
{
    /// <summary>Permission names the user inherits from active groups they belong to.</summary>
    Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Which groups grant each permission for this user, keyed by permission id.
    /// Lets the access-control UI explain a grant it cannot revoke on the user screen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetGrantingGroupsAsync(Guid userId, CancellationToken cancellationToken);
}
