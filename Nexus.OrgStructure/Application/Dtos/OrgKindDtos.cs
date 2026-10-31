namespace Nexus.OrgStructure.Application.Dtos;

/// <summary>The two node types that are the kinds «واحد سازمانی» and «سمت / پست سازمانی» (made when missing).</summary>
public sealed record KindTypesDto(Guid UnitTypeId, Guid PositionTypeId);

public sealed record KindPersonDto(Guid UserId, string DisplayName, bool IsPrimary);

/// <summary>
/// A node of the simple chart. Kind: "unit" or "position". HeadNodeId: a unit's head (one of its direct
/// positions). Persons: the people of a position, the official one marked.
/// </summary>
public sealed record KindNodeDto(
    Guid Id,
    string Name,
    string Kind,
    Guid? ParentNodeId,
    Guid? CompanyId,
    bool IsActive,
    int SortOrder,
    string RowVersion,
    Guid? HeadNodeId,
    IReadOnlyList<KindPersonDto> Persons);

public sealed record KindChartDto(IReadOnlyList<KindNodeDto> Nodes);

public sealed record AddNodePersonRequest(Guid UserId);

public sealed record SetNodeHeadRequest(Guid? HeadNodeId);
