using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Application.Dtos;

public sealed record ProjectStrategyAlignmentDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid StrategyId, AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);

public sealed record CreateAlignmentRequest(Guid TenantId, Guid ProjectId, Guid StrategyId, AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);

public sealed record UpdateAlignmentRequest(AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);

/// <summary>The project's level for one strategy; a strategy with no row counts as None.</summary>
public sealed record StrategyLevelDto(Guid StrategyId, AlignmentLevel Level);

public sealed record ProjectAlignmentScoresRequest(IReadOnlyList<Guid> ProjectIds);

/// <summary>
/// How well a project fits the strategies: its level per leaf strategy and the percentage that comes out of them - each level scores
/// 0 (None) to 3 (High), weighted by what the strategy carries, as a share of the best possible score.
/// </summary>
public sealed record ProjectAlignmentScoreDto(Guid ProjectId, IReadOnlyList<StrategyLevelDto> Levels, decimal Percentage);

/// <summary>Sets the project's level for several leaf strategies at once; strategies not listed keep what they have.</summary>
public sealed record SetProjectAlignmentRequest(Guid TenantId, Guid ProjectId, IReadOnlyList<StrategyLevelDto> Levels);
