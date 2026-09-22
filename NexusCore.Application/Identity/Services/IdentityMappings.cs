using NexusCore.Application.Identity.Dtos;
using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Services;

internal static class IdentityMappings
{
    public static UserDto ToUserDto(User user) =>
        new(
            user.Id,
            user.TenantId,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.LastLoginAtUtc,
            user.Roles.Select(role => role.Role?.Name ?? role.RoleId.ToString()).ToList(),
            user.Username,
            user.PhoneNumber,
            user.NotifySms,
            user.AvatarUrl,
            user.Theme,
            user.ColorPalette,
            user.ThemeMode);
}
