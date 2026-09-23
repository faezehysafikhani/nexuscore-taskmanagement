namespace NexusCore.Application.Identity.Dtos;

/// <summary>
/// The permissions set on the user directly: <paramref name="PermissionIds"/> are granted,
/// <paramref name="DeniedPermissionIds"/> are denied even when a role or group grants them
/// (null keeps the user's current denials, for clients that only manage grants).
/// </summary>
public sealed record AssignUserPermissionsRequest(IReadOnlyList<Guid> PermissionIds, IReadOnlyList<Guid>? DeniedPermissionIds = null);

/// <summary>
/// Breakdown of a single permission for one user, so the UI can show WHY it is granted, whether
/// it is denied to the user, and whether the user actually has it (<see cref="Effective"/>, as
/// the server enforces it). <see cref="GrantedAsPrerequisite"/>: effective only because another
/// permission the user has needs it (e.g. users.update needs users.view).
/// </summary>
public sealed record UserPermissionEntryDto(
    Guid PermissionId,
    string Name,
    string Module,
    string Description,
    bool GrantedDirectly,
    bool GrantedByRole,
    IReadOnlyList<string> GrantingRoles,
    bool GrantedByGroup,
    IReadOnlyList<string> GrantingGroups,
    bool DeniedForUser = false,
    bool Effective = false,
    bool GrantedAsPrerequisite = false);

public sealed record UserPermissionsDto(
    Guid UserId,
    string DisplayName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserPermissionEntryDto> Permissions);
