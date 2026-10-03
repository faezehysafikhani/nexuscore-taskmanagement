namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// The signed-in user's own profile. Identity fields are accepted only when unchanged for
/// compatibility with older clients; this endpoint updates contact details and avatar only.
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
