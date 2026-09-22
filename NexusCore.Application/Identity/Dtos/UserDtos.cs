namespace NexusCore.Application.Identity.Dtos;

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string? Email,
    string DisplayName,
    bool IsActive,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<string> Roles,
    string? Username = null,
    string? PhoneNumber = null,
    bool NotifySms = true,
    string? AvatarUrl = null,
    string? Theme = null,
    string? ColorPalette = null,
    string? ThemeMode = null);

/// <summary>
/// A new account needs a username (its sign-in name); mobile number and email are optional.
/// </summary>
public sealed record CreateUserRequest(
    Guid TenantId,
    string Username,
    string DisplayName,
    string Password,
    bool IsActive = true,
    string? Email = null,
    string? PhoneNumber = null,
    bool NotifySms = true);

/// <summary>
/// DisplayName and IsActive keep their original meaning. Every other field is optional:
/// null leaves it unchanged, an empty string clears Email or PhoneNumber. Username can be
/// changed but not removed. Password, when given, replaces the password and signs the user
/// out of every session.
/// </summary>
public sealed record UpdateUserRequest(
    string DisplayName,
    bool IsActive,
    string? Email = null,
    string? Password = null,
    string? Username = null,
    string? PhoneNumber = null,
    bool? NotifySms = null);

public sealed record AssignUserRolesRequest(IReadOnlyList<Guid> RoleIds);
