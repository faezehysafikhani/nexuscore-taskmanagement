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
    string? ThemeMode = null,
    string? FirstName = null,
    string? LastName = null,
    bool IsSystem = false);

/// <summary>
/// A new account, created by an administrator (there is no self-registration). Username is the
/// person's national code (10 digits); first name, last name and mobile number are required.
/// The account can never be a system account.
/// </summary>
public sealed record CreateUserRequest(
    Guid TenantId,
    string Username,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Password,
    bool IsActive = true,
    string? Email = null,
    bool NotifySms = true);

/// <summary>
/// DisplayName and IsActive keep their original meaning. Every other field is optional:
/// null leaves it unchanged, an empty string clears Email or PhoneNumber. A changed Username
/// must be a national code. FirstName/LastName, when given, set DisplayName. Password, when
/// given, replaces the password and signs the user out of every session.
/// </summary>
public sealed record UpdateUserRequest(
    string DisplayName,
    bool IsActive,
    string? Email = null,
    string? Password = null,
    string? Username = null,
    string? PhoneNumber = null,
    bool? NotifySms = null,
    string? FirstName = null,
    string? LastName = null);

public sealed record SetUserStatusRequest(bool IsActive);

public sealed record AssignUserRolesRequest(IReadOnlyList<Guid> RoleIds);
