namespace Nexus.Organization.Application.Dtos;

public sealed record OrganizationUnitDto(Guid Id, Guid TenantId, string Name, Guid? ManagerUserId, bool IsActive);

public sealed record CreateOrganizationUnitRequest(Guid TenantId, string Name);

public sealed record UpdateOrganizationUnitRequest(string Name, Guid? ManagerUserId, bool IsActive);
