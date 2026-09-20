using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class User : AuditableEntity<Guid>
{
    private readonly List<UserRole> _roles = [];
    private readonly List<RefreshToken> _refreshTokens = [];

    private User() : base(Guid.Empty)
    {
        Email = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(Guid id, Guid tenantId, string email, string displayName, string passwordHash, bool isActive = true) : base(id)
    {
        TenantId = tenantId;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        IsActive = isActive;
    }

    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    public void UpdateProfile(string displayName, bool isActive)
    {
        DisplayName = displayName.Trim();
        IsActive = isActive;
    }

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;

    public void MarkLoggedIn(DateTimeOffset loggedInAtUtc) => LastLoginAtUtc = loggedInAtUtc;

    public void AssignRole(Guid roleId)
    {
        if (_roles.All(role => role.RoleId != roleId))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        _roles.Clear();
        foreach (var roleId in roleIds.Distinct())
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    public RefreshToken AddRefreshToken(string tokenHash, DateTimeOffset expiresAtUtc, string? createdByIp)
    {
        var refreshToken = new RefreshToken(Guid.NewGuid(), Id, tokenHash, expiresAtUtc, createdByIp);
        _refreshTokens.Add(refreshToken);
        return refreshToken;
    }
}
