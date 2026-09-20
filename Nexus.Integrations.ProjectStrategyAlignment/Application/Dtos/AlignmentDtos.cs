using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Application.Dtos;

public sealed record ProjectStrategyAlignmentDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid StrategyId, AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);

public sealed record CreateAlignmentRequest(Guid TenantId, Guid ProjectId, Guid StrategyId, AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);

public sealed record UpdateAlignmentRequest(AlignmentLevel AlignmentLevel, decimal? AlignmentPercentage);
