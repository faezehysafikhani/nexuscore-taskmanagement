using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Options;

/// <summary>
/// Identity:ManagedPermissionModules - the permission modules the administrators of THIS product
/// manage (e.g. ["Identity", "Platform", "TaskManagement"]). Every module's permissions still
/// exist in the database for the products that use them; this only decides which ones this
/// host lists and lets anyone assign to users, roles and groups. Empty (the default) means all
/// modules, so a host that does not set it behaves exactly as before.
///
/// Permissions of other modules that a user, role or group already holds are left as they are
/// when its managed permissions are saved, so nothing another product set up is lost.
/// </summary>
public sealed class ManagedPermissionOptions
{
    public const string SectionName = "Identity";

    public List<string> ManagedPermissionModules { get; set; } = [];

    public bool IsManaged(Permission permission) =>
        ManagedPermissionModules.Count == 0
        || ManagedPermissionModules.Contains(permission.Module, StringComparer.OrdinalIgnoreCase);

    /// <summary>The managed ones of <paramref name="permissions"/>.</summary>
    public IReadOnlyList<Permission> Managed(IEnumerable<Permission> permissions) => permissions.Where(IsManaged).ToList();
}
