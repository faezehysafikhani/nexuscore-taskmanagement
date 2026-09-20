namespace NexusCore.Application.Identity.Dtos;

public sealed record AssignUserPermissionsRequest(IReadOnlyList<Guid> PermissionIds);

/// <summary>
/// Breakdown of a single permission for one user, so the UI can show WHY it is granted
/// and only allow the direct grant to be toggled.
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
    IReadOnlyList<string> GrantingGroups);

public sealed record UserPermissionsDto(
    Guid UserId,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserPermissionEntryDto> Permissions);
