namespace Nexus.OrgStructure.Application.Dtos;

public sealed record PersonRef(Guid UserId, string DisplayName, bool IsActive, string? AvatarUrl = null);

public sealed record CompanyDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Code,
    string? Description,
    PersonRef? Manager,
    bool IsActive,
    IReadOnlyList<Guid> LinkedUnitIds);

public sealed record CreateCompanyRequest(string Name, string Code, string? Description, Guid? ManagerUserId);

public sealed record UpdateCompanyRequest(string Name, string Code, string? Description, Guid? ManagerUserId, bool IsActive);

/// <summary>A unit linked to a company: it and every unit below it belong to that company.</summary>
public sealed record CompanyUnitDto(Guid UnitId, string UnitName, Guid CompanyId, string CompanyName);

public sealed record LinkUnitRequest(Guid CompanyId);

public sealed record PositionDto(Guid Id, string Title, Guid? ParentPositionId, bool IsActive);

public sealed record CreatePositionRequest(string Title, Guid? ParentPositionId = null);

public sealed record UpdatePositionRequest(string Title, bool IsActive, Guid? ParentPositionId = null);

/// <summary>Source: "Explicit" (set on the assignment), "UnitManager" or "CompanyManager".</summary>
public sealed record ManagerDto(Guid UserId, string DisplayName, bool IsActive, string Source);

public sealed record UserAssignmentDto(
    Guid UserId,
    string DisplayName,
    bool IsUserActive,
    Guid CompanyId,
    string CompanyName,
    Guid? UnitId,
    string? UnitName,
    Guid? PositionId,
    string? PositionTitle,
    Guid? ExplicitManagerUserId,
    ManagerDto? Manager);

public sealed record SetUserAssignmentRequest(Guid CompanyId, Guid? UnitId, Guid? PositionId, Guid? ManagerUserId);

/// <summary>Someone below a manager. Depth 1 = a direct report.</summary>
public sealed record SubordinateDto(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    int Depth,
    Guid ManagerUserId,
    string CompanyName,
    string? UnitName,
    string? PositionTitle);

/// <summary>The signed-in user's place: their assignment and their managers, nearest first.</summary>
public sealed record MyPlacementDto(UserAssignmentDto? Assignment, IReadOnlyList<PersonRef> ManagementChain);

public sealed record OrgChartMemberDto(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    string? PositionTitle,
    Guid? ManagerUserId,
    string? AvatarUrl = null,
    Guid? PositionId = null);

/// <summary>
/// A work team (one of the personal teams people build for their tasks and chat), shown under
/// the unit of the person who built it, with its members - wherever they sit.
/// </summary>
public sealed record OrgChartTeamDto(
    Guid TeamId,
    string Name,
    string? Description,
    PersonRef Owner,
    IReadOnlyList<OrgChartMemberDto> Members);

/// <summary>
/// Members: the unit's people who are in no work team. Teams: the work teams built by the unit's
/// people (a person can be in several, so may appear more than once).
/// </summary>
public sealed record OrgChartUnitDto(
    Guid UnitId,
    string Name,
    PersonRef? Manager,
    IReadOnlyList<OrgChartMemberDto> Members,
    IReadOnlyList<OrgChartUnitDto> Children,
    IReadOnlyList<OrgChartTeamDto> Teams,
    bool IsActive = true);

public sealed record OrgChartCompanyDto(
    Guid CompanyId,
    string Name,
    string Code,
    bool IsActive,
    PersonRef? Manager,
    IReadOnlyList<OrgChartMemberDto> Members,
    IReadOnlyList<OrgChartUnitDto> Units);

/// <summary>Someone of the organization who has no place in the structure yet.</summary>
public sealed record UnplacedUserDto(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record HonorificDto(Guid Id, string Title, int SortOrder, bool IsActive, int UserCount);

public sealed record CreateHonorificRequest(string Title, int? SortOrder = null);

public sealed record UpdateHonorificRequest(string Title, int SortOrder, bool IsActive);

public sealed record UserTitleDto(Guid UserId, Guid? HonorificId, string? Title);

/// <summary>Null removes the user's title.</summary>
public sealed record SetUserTitleRequest(Guid? HonorificId);
