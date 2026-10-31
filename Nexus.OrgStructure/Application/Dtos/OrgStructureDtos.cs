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
    IReadOnlyList<Guid> LinkedUnitIds,
    Guid? ParentCompanyId = null);

/// <summary>
/// The head is not chosen here: see <see cref="PositionDto"/>. TemplateId (optional): a structure
/// template (GET /api/org-structure/templates) whose types, rules, positions, nodes and links the
/// new company gets a copy of.
/// </summary>
public sealed record CreateCompanyRequest(string Name, string Code, string? Description, Guid? TemplateId = null, Guid? ParentCompanyId = null);

/// <summary>The company's parent in a group (holding); null: a company of its own.</summary>
public sealed record SetParentCompanyRequest(Guid? ParentCompanyId);

/// <summary>A company and the companies under it in its group, at any depth.</summary>
public sealed record CompanyGroupDto(Guid Id, string Name, string Code, bool IsActive, IReadOnlyList<CompanyGroupDto> Children);

public sealed record UpdateCompanyRequest(string Name, string Code, string? Description, bool IsActive);

/// <summary>A unit linked to a company: it and every unit below it belong to that company.</summary>
public sealed record CompanyUnitDto(Guid UnitId, string UnitName, Guid CompanyId, string CompanyName);

public sealed record LinkUnitRequest(Guid CompanyId);

/// <summary>
/// CompanyIds: the companies the position is for. In each of them, the one position with no
/// superior (ParentPositionId) is the head's: whoever holds it directly in the company is the
/// company's head (رئیس کل).
/// </summary>
public sealed record PositionDto(Guid Id, string Title, Guid? ParentPositionId, bool IsActive, IReadOnlyList<Guid>? CompanyIds = null);

/// <summary>CompanyIds: at least one. A superior must be for every one of them.</summary>
public sealed record CreatePositionRequest(string Title, Guid? ParentPositionId = null, IReadOnlyList<Guid>? CompanyIds = null);

public sealed record UpdatePositionRequest(string Title, bool IsActive, Guid? ParentPositionId = null, IReadOnlyList<Guid>? CompanyIds = null);

/// <summary>Source: "Explicit" (set on the assignment), "UnitManager" or "CompanyManager".</summary>
public sealed record ManagerDto(Guid UserId, string DisplayName, bool IsActive, string Source);

/// <summary>
/// PositionId / PositionTitle: the person's main position. Positions: every unit position they
/// hold (the main one first), which may be in other units too.
/// </summary>
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
    ManagerDto? Manager,
    IReadOnlyList<HeldPositionDto>? Positions = null);

/// <summary>
/// UnitPositionIds, when sent, is every unit position the person holds (replacing the list); the
/// main position is PositionId if it is one of them, else the first in UnitId, else the first.
/// Without it, PositionId in
/// UnitId is their main position, added to the unit's positions when the unit lacks it, and
/// their other positions stay as they are.
/// </summary>
public sealed record SetUserAssignmentRequest(
    Guid CompanyId, Guid? UnitId, Guid? PositionId, Guid? ManagerUserId, IReadOnlyList<Guid>? UnitPositionIds = null, DateTimeOffset? StartDate = null, DateTimeOffset? EndDate = null);

/// <summary>A post someone holds. UnitId null: directly in the company (UnitName is then the company's).</summary>
public sealed record HeldPositionDto(Guid UnitPositionId, Guid? UnitId, string UnitName, Guid PositionId, string Title, string? Code = null);

/// <summary>
/// A post, in its unit's order, with whoever holds it (none: vacant; never more than one).
/// UnitId null: directly in the company.
/// </summary>
public sealed record UnitPositionDto(
    Guid Id,
    Guid? UnitId,
    string? UnitName,
    Guid PositionId,
    string Title,
    int SortOrder,
    bool IsPositionActive,
    IReadOnlyList<PersonRef> Holders,
    string Code = "",
    Guid CompanyId = default,
    string? CompanyName = null,
    Guid? ParentPostId = null,
    bool IsManagerPost = false);

/// <summary>Code: optional; without it the next free number of the company. ParentPostId: the post above it.</summary>
public sealed record AddUnitPositionRequest(Guid PositionId, string? Code = null, Guid? ParentPostId = null);

/// <summary>The post above this one (same unit, or both directly in the company); null: none.</summary>
public sealed record SetPostParentRequest(Guid? ParentPostId);

public sealed record UpdateUnitPositionRequest(string Code);

/// <summary>Every position of the unit, in the new order.</summary>
public sealed record OrderUnitPositionsRequest(IReadOnlyList<Guid> UnitPositionIds);

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
/// Positions: the unit's positions in its order, each with its holders (vacant ones too).
/// Members: the unit's people who hold none of its positions. Teams: the work teams built by the
/// unit's people (no longer drawn in the chart; kept for older clients).
/// Children: the nodes directly under it (Units of the company holds only its top nodes), in their
/// order; ParentNodeId: the node above (null at the top); NodeType and Links: its type and its
/// secondary links (null / empty for a plain unit with none).
/// </summary>
public sealed record OrgChartUnitDto(
    Guid UnitId,
    string Name,
    PersonRef? Manager,
    IReadOnlyList<OrgChartMemberDto> Members,
    IReadOnlyList<OrgChartUnitDto> Children,
    IReadOnlyList<OrgChartTeamDto> Teams,
    bool IsActive = true,
    int PeopleCount = 0,
    IReadOnlyList<OrgChartPositionDto>? Positions = null,
    Guid? ParentNodeId = null,
    OrgChartNodeTypeDto? NodeType = null,
    IReadOnlyList<OrgChartLinkDto>? Links = null);

/// <summary>A node's type as the chart draws it: colour (#rrggbb) and lucide icon name.</summary>
public sealed record OrgChartNodeTypeDto(Guid NodeTypeId, string Name, string Color, string Icon, bool IsDomainBoundary);

/// <summary>A secondary link from a node to another (LineStyle: solid, dotted or dashed).</summary>
public sealed record OrgChartLinkDto(Guid LinkId, Guid TargetNodeId, Guid LinkTypeId, string Name, string LineStyle, bool CountsAsManager);

/// <summary>A post in the chart; no holder means it is vacant. IsHead: the company head's post.</summary>
public sealed record OrgChartPositionDto(
    Guid UnitPositionId,
    Guid PositionId,
    string Title,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<OrgChartMemberDto> Holders,
    string Code = "",
    bool IsHead = false,
    Guid? ParentPostId = null);

/// <summary>
/// Members: the company's people in no unit who hold none of its own posts. Positions: the posts
/// directly in the company (the head's first).
/// </summary>
public sealed record OrgChartCompanyDto(
    Guid CompanyId,
    string Name,
    string Code,
    bool IsActive,
    PersonRef? Manager,
    IReadOnlyList<OrgChartMemberDto> Members,
    IReadOnlyList<OrgChartUnitDto> Units,
    int PeopleCount = 0,
    IReadOnlyList<OrgChartPositionDto>? Positions = null,
    string? UiLevel = null);

/// <summary>Someone of the organization who has no place in the structure yet.</summary>
public sealed record UnplacedUserDto(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record HonorificDto(Guid Id, string Title, int SortOrder, bool IsActive, int UserCount);

public sealed record CreateHonorificRequest(string Title, int? SortOrder = null);

public sealed record UpdateHonorificRequest(string Title, int SortOrder, bool IsActive);

public sealed record UserTitleDto(Guid UserId, Guid? HonorificId, string? Title);

/// <summary>Null removes the user's title.</summary>
public sealed record SetUserTitleRequest(Guid? HonorificId);

// ---------------------------------------------------------------- posts from a pattern (phase 6.1)

public sealed record SetManagerPostRequest(bool IsManagerPost);

/// <summary>A post a node type gets: of this position; IsManagerPost: the node's manager post.</summary>
public sealed record NodeTypePostDto(Guid PositionId, string Title, bool IsManagerPost, int SortOrder);

public sealed record NodeTypePostRequest(Guid PositionId, bool IsManagerPost = false);

/// <summary>The posts, in order; at most one manager post.</summary>
public sealed record SetNodeTypePostsRequest(IReadOnlyList<NodeTypePostRequest> Posts);

/// <summary>One node (NodeId) or every active node of a type (NodeTypeId).</summary>
public sealed record ApplyPostTemplatesRequest(Guid? NodeId = null, Guid? NodeTypeId = null);

/// <summary>A post of the pattern that was not made, and why (a post the node already has is not listed).</summary>
public sealed record SkippedPostDto(Guid NodeId, string NodeName, string? PositionTitle, string Reason);

public sealed record ApplyPostTemplatesResultDto(IReadOnlyList<UnitPositionDto> Created, IReadOnlyList<SkippedPostDto> Skipped);

/// <summary>A post a template puts directly in the company (resolved to the organization's position).</summary>
public sealed record CompanyPostToMake(Guid PositionId, bool IsCompanyHead);

// ---------------------------------------------------------------- appointments over time (phase 9)

/// <summary>UserId takes the post from StartDate (null: now; never in the past). Whoever holds it then ends on that day.</summary>
public sealed record AppointRequest(Guid UserId, DateTimeOffset? StartDate = null);

/// <summary>The post's appointment on now ends on EndDate (null: now; never in the past).</summary>
public sealed record EndAppointmentRequest(DateTimeOffset? EndDate = null);

/// <summary>One appointment: who held (holds, will hold) which post, from when until when (null: no limit).</summary>
public sealed record AppointmentDto(
    Guid Id, Guid UnitPositionId, string? PostTitle, string? PostCode, string? Place, PersonRef Person,
    DateTimeOffset? StartDate, DateTimeOffset? EndDate, bool IsActive);
