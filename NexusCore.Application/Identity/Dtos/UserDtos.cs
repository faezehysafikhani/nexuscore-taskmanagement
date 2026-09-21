namespace NexusCore.Application.Identity.Dtos;

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    string DisplayName,
    bool IsActive,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<string> Roles,
    string? Username = null,
    string? PhoneNumber = null,
    string? TelegramChatId = null,
    bool NotifySms = true,
    bool NotifyTelegram = true,
    string? AvatarUrl = null,
    string? Theme = null,
    string? ColorPalette = null,
    string? ThemeMode = null);

/// <summary>
/// Profile fields are optional so existing callers that send only the original four keep
/// working unchanged.
/// </summary>
public sealed record CreateUserRequest(
    Guid TenantId,
    string Email,
    string DisplayName,
    string Password,
    bool IsActive = true,
    string? Username = null,
    string? PhoneNumber = null,
    string? TelegramChatId = null,
    bool NotifySms = true,
    bool NotifyTelegram = true);

/// <summary>
/// DisplayName and IsActive keep their original meaning. Every other field is optional:
/// null leaves it unchanged. Password, when given, replaces the password and signs the user
/// out of every session.
/// </summary>
public sealed record UpdateUserRequest(
    string DisplayName,
    bool IsActive,
    string? Email = null,
    string? Password = null,
    string? Username = null,
    string? PhoneNumber = null,
    string? TelegramChatId = null,
    bool? NotifySms = null,
    bool? NotifyTelegram = null);

public sealed record AssignUserRolesRequest(IReadOnlyList<Guid> RoleIds);
