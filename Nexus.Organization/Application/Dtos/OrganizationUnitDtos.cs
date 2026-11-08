namespace Nexus.Organization.Application.Dtos;

/// <param name="Code">The unit's own code (the structure's node code); empty when it has none.</param>
/// <param name="ParentId">The unit above it in the chart (the structure's parent); null at the top.</param>
/// <param name="Path">Its full path from the top of the chart.</param>
public sealed record OrganizationUnitDto(
    Guid Id, Guid TenantId, string Name, Guid? ManagerUserId, bool IsActive,
    string Code = "", Guid? ParentId = null, string? Path = null);

/// <summary>
/// <paramref name="Code"/> and <paramref name="ParentId"/> are accepted for screens that send the whole unit: the unit's
/// place and code belong to the organization structure module, so a value that differs from the unit's current one is refused.
/// </summary>
public sealed record CreateOrganizationUnitRequest(Guid TenantId, string Name, string? Code = null, Guid? ParentId = null);

/// <summary>An omitted (null) <paramref name="Code"/> or <paramref name="ParentId"/> means "as it is"; see <see cref="CreateOrganizationUnitRequest"/>.</summary>
public sealed record UpdateOrganizationUnitRequest(string Name, Guid? ManagerUserId, bool IsActive, string? Code = null, Guid? ParentId = null);

/// <summary>A person's unit: its name and its full path from the top of the chart.</summary>
public sealed record OrganizationMemberDto(Guid UserId, Guid UnitId, string UnitName, string UnitPath);

/// <summary>UnitId null takes the user out of the chart.</summary>
public sealed record SetUserUnitRequest(Guid? UnitId);
