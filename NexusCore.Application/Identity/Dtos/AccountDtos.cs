namespace NexusCore.Application.Identity.Dtos;

/// <summary>Self-registration. Only accepted when Identity:SelfRegistration:Enabled is true.</summary>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string? Username = null,
    string? PhoneNumber = null,
    string? TelegramChatId = null,
    bool NotifySms = true,
    bool NotifyTelegram = true,
    string? Theme = null,
    string? ColorPalette = null,
    string? ThemeMode = null);

/// <summary>The signed-in user's own profile. Null clears an optional field.</summary>
public sealed record UpdateMyProfileRequest(
    string DisplayName,
    string? Username = null,
    string? AvatarUrl = null,
    string? PhoneNumber = null,
    string? TelegramChatId = null,
    bool NotifySms = true,
    bool NotifyTelegram = true);

public sealed record UpdateMyPreferencesRequest(string? Theme, string? ColorPalette, string? ThemeMode);
