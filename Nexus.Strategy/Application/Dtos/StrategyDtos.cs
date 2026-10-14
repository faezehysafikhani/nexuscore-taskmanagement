namespace Nexus.StrategyManagement.Application.Dtos;

public sealed record StrategyDto(Guid Id, Guid TenantId, string Name, string? Description, decimal Weight, Guid? ParentStrategyId);

public sealed record CreateStrategyRequest(Guid TenantId, string Name, string? Description, decimal Weight, Guid? ParentStrategyId);

public sealed record UpdateStrategyRequest(string Name, string? Description, decimal Weight, Guid? ParentStrategyId);

/// <summary>One strategy in the tree. EffectiveWeight is what it carries: its own for a leaf, the sum of its sub-strategies' otherwise.</summary>
public sealed record StrategyNodeDto(
    Guid Id, string Name, string? Description, decimal StoredWeight, decimal EffectiveWeight, Guid? ParentStrategyId, int Level, bool IsLeaf, int SortOrder);

/// <summary>The strategies in tree order (a parent before its children) and the total weight of the top-level ones.</summary>
public sealed record StrategyTreeDto(IReadOnlyList<StrategyNodeDto> Nodes, decimal TotalWeight);

/// <summary>
/// One strategy of a whole tree being saved. Key identifies it within the request: the id of an existing strategy to update it, anything
/// else (a temporary label) for a new one. ParentKey is the Key of its parent.
/// </summary>
public sealed record SaveStrategyTreeItem(string Key, string Name, string? Description, decimal Weight, string? ParentKey);

/// <summary>The whole tree as it should be: strategies not listed are deleted. Order in the list is the order shown.</summary>
public sealed record SaveStrategyTreeRequest(Guid TenantId, IReadOnlyList<SaveStrategyTreeItem> Items);
