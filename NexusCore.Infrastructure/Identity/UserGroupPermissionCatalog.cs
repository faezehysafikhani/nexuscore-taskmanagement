using NexusCore.Application.Identity.Permissions;

namespace NexusCore.Infrastructure.Identity;

/// <summary>
/// Registered only when the user-group feature is enabled - see
/// <see cref="UserGroupServiceCollectionExtensions.AddUserGroupFeature"/>. With the feature
/// off, this type is never constructed and groups.* permissions are never seeded.
/// </summary>
public sealed class UserGroupPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => UserGroupPermissions.All;
}
