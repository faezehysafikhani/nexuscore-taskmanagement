namespace NexusCore.Domain.Identity;

/// <summary>
/// A permission granted directly to a user, independent of their roles.
/// Effective permissions = role-derived permissions UNION direct grants.
/// Mirrors <see cref="RolePermission"/>.
/// </summary>
public sealed class UserPermission
{
    private UserPermission()
    {
    }

    public UserPermission(Guid userId, Guid permissionId)
    {
        UserId = userId;
        PermissionId = permissionId;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission? Permission { get; private set; }
}
