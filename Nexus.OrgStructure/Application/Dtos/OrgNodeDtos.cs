using System.Text.Json.Serialization;

namespace Nexus.OrgStructure.Application.Dtos;

// The node API (/api/org-structure/nodes, /node-types, /node-type-rules). RowVersion is the node's
// version as base64: send back the one you read, and a save over someone else's newer change is
// refused (409); leave it out to skip that check.

/// <summary>
/// A node (a unit: deputy, general department, committee...). ParentNodeId null: at the top, under
/// its company (CompanyId, through its link) or under none. CompanyId: the company it belongs to,
/// through itself or the nearest node above it that is linked to one. AutoLinkChoices (only right
/// after it is made): automatic links with several possible targets, for the user to choose.
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
    string RowVersion,
    IReadOnlyList<AutoLinkChoiceDto>? AutoLinkChoices = null,
    string? Code = null,
    string? LatinName = null,
    string? CorrespondenceTitle = null,
    string? Phone = null,
    string? Address = null);

/// <summary>ParentNodeId null: a top node, linked to CompanyId when given. NodeTypeId null: the plain «واحد» type.</summary>
public sealed record CreateNodeRequest(string Name, Guid? ParentNodeId = null, Guid? CompanyId = null, Guid? NodeTypeId = null, string? Code = null);

/// <summary>The node's code (unique in its company) and optional details; each null or empty: none. Replaces them all.</summary>
public sealed record SetNodeDetailsRequest(
    string? Code = null, string? LatinName = null, string? CorrespondenceTitle = null, string? Phone = null, string? Address = null, string? RowVersion = null);

public sealed record UpdateNodeRequest(string Name, Guid? NodeTypeId = null, string? RowVersion = null);

/// <summary>ParentNodeId null: to the top of CompanyId (or of its present company when CompanyId is null too).</summary>
/// parentNodeId must be in the body (null: to the top), so an empty or misspelt body is refused, not taken as «to the top».
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MoveNodeRequest([property: JsonRequired] Guid? ParentNodeId, Guid? CompanyId = null, string? RowVersion = null);

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

// ---------------------------------------------------------------- secondary links (phase 4)

/// <summary>A kind of secondary link. LineStyle: solid, dotted or dashed. A dotted one never counts as manager.</summary>
public sealed record LinkTypeDto(Guid Id, string Code, string Name, string LineStyle, bool CountsAsManager, bool IsActive, int LinkCount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLinkTypeRequest([property: JsonRequired] string Name, string? Code = null, string LineStyle = "dotted", bool CountsAsManager = false);

/// <summary>Code: accepted and ignored (a type's code does not change).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLinkTypeRequest(
    [property: JsonRequired] string Name, [property: JsonRequired] string LineStyle, bool CountsAsManager, bool IsActive, string? Code = null);

/// <summary>A node of the source type, when made, is linked to the one node of the target type (in its company).</summary>
public sealed record AutoLinkRuleDto(Guid SourceNodeTypeId, Guid TargetNodeTypeId, Guid LinkTypeId);

public sealed record SetAutoLinkRulesRequest(IReadOnlyList<AutoLinkRuleDto> Rules);

public sealed record OrgLinkDto(
    Guid Id, Guid FromNodeId, string FromName, Guid ToNodeId, string ToName, Guid LinkTypeId, string LinkTypeName, string LineStyle, bool CountsAsManager);

public sealed record CreateLinkRequest(Guid FromNodeId, Guid ToNodeId, Guid LinkTypeId);

/// <summary>An automatic link with several possible targets: the user picks one (POST /links).</summary>
public sealed record AutoLinkChoiceDto(Guid NodeId, string NodeName, Guid LinkTypeId, string LinkTypeName, IReadOnlyList<NodePathItemDto> Targets);

public sealed record ProposedLinkDto(Guid FromNodeId, string FromName, Guid ToNodeId, string ToName, Guid LinkTypeId, string LinkTypeName);

/// <summary>What the automatic rules would add to the existing nodes: Links (one target each) and Ambiguous (several targets: not added).</summary>
public sealed record AutoLinkPlanDto(IReadOnlyList<ProposedLinkDto> Links, IReadOnlyList<AutoLinkChoiceDto> Ambiguous);

/// <summary>The links of a plan to add (each must still be in the plan).</summary>
public sealed record ApplyAutoLinksRequest(IReadOnlyList<CreateLinkRequest> Links);

/// <summary>simple, normal or advanced.</summary>
public sealed record SetUiLevelRequest(string UiLevel);

// ---------------------------------------------------------------- templates (phase 4)

/// <summary>A structure template: built in (IsSystem, from the product) or saved from a company of this organization.</summary>
public sealed record StructureTemplateDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, string UiLevel, int NodeCount, int NodeTypeCount);

/// <summary>The template and its whole content (types, rules, positions, nodes...), as JSON.</summary>
public sealed record StructureTemplateDetailDto(StructureTemplateDto Template, System.Text.Json.JsonElement Content);

/// <summary>Saves a company's structure as a template of this organization (a copy).</summary>
public sealed record SaveStructureTemplateRequest(Guid CompanyId, string Name, string? Code = null, string? Description = null);

// ---------------------------------------------------------------- import from a sheet (phase 6.2)

/// <summary>Where the sheet's top rows go: the top of CompanyId, or under ParentNodeId (one of the two).</summary>
public sealed record ImportTargetRequest(Guid? CompanyId = null, Guid? ParentNodeId = null);

/// <summary>A fault of the sheet; Row: its row in the sheet (null: the file as a whole).</summary>
public sealed record ImportErrorDto(int? Row, string? Code, string Message);

public sealed record ImportNodePreviewDto(int Row, string Code, string Name, string? ParentCode, string TypeName, string? ManagerPosition, int Depth);

/// <summary>What the sheet would make. Nothing is made while Errors is not empty (all or nothing).</summary>
public sealed record ImportPreviewDto(int RowCount, IReadOnlyList<ImportNodePreviewDto> Nodes, IReadOnlyList<ImportErrorDto> Errors);

/// <summary>What was made: nodes, automatic links, posts (manager posts of the sheet and each type's posts), and links left to choose.</summary>
public sealed record ImportResultDto(IReadOnlyList<OrgNodeDto> Nodes, int Links, int Posts, IReadOnlyList<AutoLinkChoiceDto> AutoLinkChoices, IReadOnlyList<SkippedPostDto> SkippedPosts);

// ---------------------------------------------------------------- dates, merge, log (phase 6.4)

/// <summary>From when and until when the node exists (each null: no limit). Only for the chart on a day (asOf).</summary>
public sealed record NodeValidityRequest(DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidTo = null, string? RowVersion = null);

/// <summary>The node goes into TargetNodeId (same company). EffectiveDate: the day it ends (null: now). RowVersion: the source's.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MergeNodeRequest([property: JsonRequired] Guid TargetNodeId, DateTimeOffset? EffectiveDate = null, string? RowVersion = null);

/// <summary>One change of a node, newest first. Details: what changed (JSON). CanUndo: the kind of change can be undone (only the last one is).</summary>
public sealed record NodeChangeDto(Guid Id, string Action, Guid? UserId, DateTimeOffset AtUtc, string? Details, bool CanUndo);
