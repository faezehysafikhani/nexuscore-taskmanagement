namespace Nexus.OrgStructure.Application.Dtos;

// The node API (/api/org-structure/nodes, /node-types, /node-type-rules). RowVersion is the node's
// version as base64: send back the one you read, and a save over someone else's newer change is
// refused (409); leave it out to skip that check.

/// <summary>
/// A node (a unit: deputy, general department, committee...). ParentNodeId null: at the top, under
/// its company (CompanyId, through its link) or under none. CompanyId: the company it belongs to,
/// through itself or the nearest node above it that is linked to one.
/// </summary>
public sealed record OrgNodeDto(
    Guid Id,
    string Name,
    Guid? ParentNodeId,
    Guid? NodeTypeId,
    string? NodeTypeName,
    Guid? CompanyId,
    Guid? ManagerUserId,
    bool IsActive,
    int SortOrder,
    int ChildCount,
    string RowVersion);

/// <summary>ParentNodeId null: a top node, linked to CompanyId when given. NodeTypeId null: the plain «واحد» type.</summary>
public sealed record CreateNodeRequest(string Name, Guid? ParentNodeId = null, Guid? CompanyId = null, Guid? NodeTypeId = null);

public sealed record UpdateNodeRequest(string Name, Guid? NodeTypeId = null, string? RowVersion = null);

/// <summary>ParentNodeId null: to the top of CompanyId (or of its present company when CompanyId is null too).</summary>
public sealed record MoveNodeRequest(Guid? ParentNodeId, Guid? CompanyId = null, string? RowVersion = null);

/// <summary>Position: its new place among the nodes beside it, from 0.</summary>
public sealed record ReorderNodeRequest(int Position, string? RowVersion = null);

public sealed record SetNodeManagerRequest(Guid? ManagerUserId, string? RowVersion = null);

/// <summary>For deactivating and activating a node.</summary>
public sealed record NodeVersionRequest(string? RowVersion = null);

public sealed record NodePathItemDto(Guid Id, string Name);

/// <summary>Path: the nodes above it, from the top. MatchedOn: "name", "code" (one of its posts) or "person" (its manager or a post holder).</summary>
public sealed record NodeSearchResultDto(OrgNodeDto Node, IReadOnlyList<NodePathItemDto> Path, string MatchedOn, string? CompanyName);

/// <summary>NodeCount: how many nodes are of this type.</summary>
public sealed record NodeTypeDto(
    Guid Id,
    string Code,
    string Name,
    string Color,
    string Icon,
    bool IsDomainBoundary,
    int SortOrder,
    bool IsActive,
    int NodeCount);

/// <summary>Code: a short latin key (made from the order when left out). Color #rrggbb; Icon a lucide icon name.</summary>
public sealed record CreateNodeTypeRequest(string Name, string? Code = null, string? Color = null, string? Icon = null, bool IsDomainBoundary = true);

public sealed record UpdateNodeTypeRequest(string Name, string? Color, string? Icon, bool IsDomainBoundary, int SortOrder, bool IsActive);

/// <summary>A node of the child type may sit under a node of the parent type (ParentNodeTypeId null: directly under the company).</summary>
public sealed record NodeTypeRuleDto(Guid? ParentNodeTypeId, Guid ChildNodeTypeId);

public sealed record SetNodeTypeRulesRequest(IReadOnlyList<NodeTypeRuleDto> Rules);

/// <summary>A node that does not match the rules (nothing is changed or removed: it is only listed).</summary>
public sealed record RuleConflictDto(Guid NodeId, string NodeName, string? NodeTypeName, Guid? ParentNodeId, string? ParentName, string? ParentTypeName);

/// <summary>The rules and the nodes that do not match them. No rules at all: nodes stay at the top (flat).</summary>
public sealed record NodeTypeRulesDto(IReadOnlyList<NodeTypeRuleDto> Rules, IReadOnlyList<RuleConflictDto> Conflicts);
