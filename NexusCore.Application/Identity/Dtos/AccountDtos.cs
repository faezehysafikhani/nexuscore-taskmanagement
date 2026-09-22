namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// Self-registration. Only accepted when Identity:SelfRegistration:Enabled is true. Username and
/// mobile number are the sign-in names, so both are required; email is an optional contact
/// address (used for password-reset links).
/// </summary>
public sealed record RegisterRequest(
    string Username,
    string PhoneNumber,
    string Password,
    string DisplayName,
    string? Email = null,
    bool NotifySms = true,
    string? Theme = null,
    string? ColorPalette = null,
    string? ThemeMode = null);

/// <summary>
/// The signed-in user's own profile. Username stays required (it is a sign-in name); null clears
/// the other optional fields.
/// </summary>
public sealed record UpdateMyProfileRequest(
    string DisplayName,
    string Username,
    string? AvatarUrl = null,
    string? PhoneNumber = null,
    bool NotifySms = true);

public sealed record UpdateMyPreferencesRequest(string? Theme, string? ColorPalette, string? ThemeMode);
