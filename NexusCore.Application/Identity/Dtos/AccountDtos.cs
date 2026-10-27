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

/// <summary>
/// Step 1 of changing the mobile number: the new number and the current password. A code is sent
/// by SMS to the NEW number.
/// </summary>
public sealed record RequestPhoneChangeRequest(string NewPhoneNumber, string CurrentPassword);

public sealed record PhoneChangeRequestedResponse(string Message, int ExpiresInSeconds);

/// <summary>Step 2: the code that arrived on the new number, the same number and the current password.</summary>
public sealed record ConfirmPhoneChangeRequest(string NewPhoneNumber, string Code, string CurrentPassword);
