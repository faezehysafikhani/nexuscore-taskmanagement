namespace Nexus.Organization.Application.Dtos;

public sealed record OrganizationUnitDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Code,
    Guid? ParentId,
    Guid? ManagerUserId,
    bool IsActive,
    string? Path = null);

/// <summary>Code omitted = the next free code ("U0001", "U0002"...) is assigned.</summary>
public sealed record CreateOrganizationUnitRequest(Guid TenantId, string Name, string? Code = null, Guid? ParentId = null);

public sealed record UpdateOrganizationUnitRequest(string Name, string Code, Guid? ParentId, Guid? ManagerUserId, bool IsActive);

/// <summary>A person's unit: its name and its full path from the top of the chart.</summary>
public sealed record OrganizationMemberDto(Guid UserId, Guid UnitId, string UnitName, string UnitPath);

/// <summary>UnitId null takes the user out of the chart.</summary>
public sealed record SetUserUnitRequest(Guid? UnitId);
