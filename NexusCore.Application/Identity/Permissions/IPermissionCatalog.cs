namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Contributed by every installed module (Core or optional feature) so the permission rows
/// that actually need to exist can be discovered at runtime via DI, without NexusCore ever
/// taking a compile-time reference on any module.
///
/// A module that is not registered never registers a catalog entry, so its permissions are
/// never seeded and can never be granted - consistent with permissions being owned per module.
/// </summary>
public interface IPermissionCatalog
{
    IReadOnlyList<PermissionDefinition> GetPermissions();
}
