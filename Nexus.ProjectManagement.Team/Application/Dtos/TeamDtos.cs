namespace Nexus.ProjectManagement.Team.Application.Dtos;

public sealed record ProjectMemberDto(Guid Id, Guid TenantId, Guid ProjectId, Guid UserId, string? RoleTitle);

public sealed record AddProjectMemberRequest(Guid TenantId, Guid ProjectId, Guid UserId, string? RoleTitle);

public sealed record UpdateProjectMemberRequest(string? RoleTitle);

public sealed record ListMembersForProjectsRequest(IReadOnlyList<Guid> ProjectIds);

/// <summary>The whole team of a project in one go: people not listed are removed, people not yet on it are added (without a role title),
/// those already on it keep theirs.</summary>
public sealed record SetProjectTeamRequest(Guid TenantId, Guid ProjectId, IReadOnlyList<Guid> UserIds);

public sealed record GovernanceRoleDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, Guid? UserId,
    string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);

public sealed record CreateGovernanceRoleRequest(
    Guid TenantId, Guid ProjectId, string Title, Guid? UserId,
    string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);

public sealed record UpdateGovernanceRoleRequest(
    string Title, Guid? UserId, string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);
