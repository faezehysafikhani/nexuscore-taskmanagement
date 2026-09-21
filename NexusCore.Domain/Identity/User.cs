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
        NotifySms = true;
        NotifyTelegram = true;
    }

    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    /// <summary>Optional sign-in name, unique within the tenant. Email always works too.</summary>
    public string? Username { get; private set; }

    /// <summary>Mobile number used for SMS notifications.</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>Telegram chat the notification bot writes to.</summary>
    public string? TelegramChatId { get; private set; }

    public bool NotifySms { get; private set; }
    public bool NotifyTelegram { get; private set; }

    /// <summary>Avatar image as a URL or data URL (the UI's preset or uploaded picture).</summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>UI preferences, stored so they follow the user between devices.</summary>
    public string? Theme { get; private set; }
    public string? ColorPalette { get; private set; }
    public string? ThemeMode { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    public void UpdateProfile(string displayName, bool isActive)
    {
        DisplayName = displayName.Trim();
        IsActive = isActive;
    }

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;

    public void ChangeEmail(string email) => Email = email.Trim().ToLowerInvariant();

    public void UpdateContactDetails(
        string? username, string? phoneNumber, string? telegramChatId, bool notifySms, bool notifyTelegram)
    {
        Username = Normalize(username);
        PhoneNumber = Normalize(phoneNumber);
        TelegramChatId = Normalize(telegramChatId);
        NotifySms = notifySms;
        NotifyTelegram = notifyTelegram;
    }

    public void ChangeDisplayName(string displayName) => DisplayName = displayName.Trim();

    public void SetAvatar(string? avatarUrl) => AvatarUrl = Normalize(avatarUrl);

    public void SetPreferences(string? theme, string? colorPalette, string? themeMode)
    {
        Theme = Normalize(theme);
        ColorPalette = Normalize(colorPalette);
        ThemeMode = Normalize(themeMode);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
