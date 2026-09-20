namespace Nexus.ProjectManagement.Team.Application.Dtos;

public sealed record ProjectMemberDto(Guid Id, Guid TenantId, Guid ProjectId, Guid UserId, string? RoleTitle);

public sealed record AddProjectMemberRequest(Guid TenantId, Guid ProjectId, Guid UserId, string? RoleTitle);

public sealed record GovernanceRoleDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, Guid? UserId,
    string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);

public sealed record CreateGovernanceRoleRequest(
    Guid TenantId, Guid ProjectId, string Title, Guid? UserId,
    string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);

public sealed record UpdateGovernanceRoleRequest(
    string Title, Guid? UserId, string? PersonnelNumber, string? Phone, string? Email, string? ServiceLocation);
