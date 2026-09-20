namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Always-installed catalog entry for the Identity/Platform permissions defined in
/// <see cref="IdentityPermissions"/>. NexusCore itself is never optional, so this is
/// registered unconditionally by <see cref="DependencyInjection.AddApplication"/>.
/// </summary>
public sealed class IdentityPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => IdentityPermissions.All;
}
