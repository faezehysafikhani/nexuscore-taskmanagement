namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// The signed-in user's own profile. The username (national code) is not part of it: only an
/// administrator changes it. When FirstName/LastName are given they set DisplayName.
/// </summary>
public sealed record UpdateMyProfileRequest(
    string DisplayName,
    string? AvatarUrl = null,
    string? PhoneNumber = null,
    bool NotifySms = true,
    string? FirstName = null,
    string? LastName = null);

public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateMyPreferencesRequest(string? Theme, string? ColorPalette, string? ThemeMode);
