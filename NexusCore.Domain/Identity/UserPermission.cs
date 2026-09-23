namespace NexusCore.Domain.Identity;

/// <summary>
/// A permission set on one user directly, independent of their roles: either a grant or, with
/// <see cref="IsDenied"/>, an explicit denial. Effective permissions = (role-derived UNION group
/// UNION direct grants, with their prerequisites) MINUS the user's denials - a denial wins over
/// every other way the permission could reach the user. Mirrors <see cref="RolePermission"/>.
/// </summary>
public sealed class UserPermission
{
    private UserPermission()
    {
    }

    public UserPermission(Guid userId, Guid permissionId, bool isDenied = false)
    {
        UserId = userId;
        PermissionId = permissionId;
        IsDenied = isDenied;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission? Permission { get; private set; }

    /// <summary>True: the permission is withheld from this user even when a role or group grants it.</summary>
    public bool IsDenied { get; private set; }

    internal void SetDenied(bool isDenied) => IsDenied = isDenied;
}
