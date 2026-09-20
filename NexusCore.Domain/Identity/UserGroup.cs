using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

/// <summary>
/// A named bucket of users that carries its own permission set.
/// A third, independent grant axis alongside roles and direct user grants:
/// effective permissions = roles UNION direct grants UNION groups the user belongs to.
///
/// Part of the optional "UserGroups" feature - see UserGroupOptions. When the feature is
/// switched off nothing reads these tables and the axis contributes nothing.
/// </summary>
public sealed class UserGroup : AuditableEntity<Guid>
{
    private readonly List<UserGroupPermission> _permissions = [];
    private readonly List<UserGroupMember> _members = [];

    private UserGroup() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public UserGroup(Guid id, Guid tenantId, string name, string? description = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
    }

    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<UserGroupPermission> Permissions => _permissions.AsReadOnly();
    public IReadOnlyCollection<UserGroupMember> Members => _members.AsReadOnly();

    public void Update(string name, string? description, bool isActive)
    {
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
        IsActive = isActive;
    }

    public void SetPermissions(IEnumerable<Guid> permissionIds)
    {
        _permissions.Clear();
        foreach (var permissionId in permissionIds.Distinct())
        {
            _permissions.Add(new UserGroupPermission(Id, permissionId));
        }
    }

    public void SetMembers(IEnumerable<Guid> userIds)
    {
        _members.Clear();
        foreach (var userId in userIds.Distinct())
        {
            _members.Add(new UserGroupMember(Id, userId));
        }
    }
}

public sealed class UserGroupPermission
{
    private UserGroupPermission()
    {
    }

    public UserGroupPermission(Guid userGroupId, Guid permissionId)
    {
        UserGroupId = userGroupId;
        PermissionId = permissionId;
    }

    public Guid UserGroupId { get; private set; }
    public UserGroup? UserGroup { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission? Permission { get; private set; }
}

public sealed class UserGroupMember
{
    private UserGroupMember()
    {
    }

    public UserGroupMember(Guid userGroupId, Guid userId)
    {
        UserGroupId = userGroupId;
        UserId = userId;
        JoinedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid UserGroupId { get; private set; }
    public UserGroup? UserGroup { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
}
