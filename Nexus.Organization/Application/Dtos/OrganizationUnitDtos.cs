namespace Nexus.Organization.Application.Dtos;

public sealed record OrganizationUnitDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Code,
    Guid? ParentId,
    Guid? ManagerUserId,
    bool IsActive);

public sealed record CreateOrganizationUnitRequest(Guid TenantId, string Name, string Code, Guid? ParentId);

public sealed record UpdateOrganizationUnitRequest(string Name, string Code, Guid? ParentId, Guid? ManagerUserId, bool IsActive);
