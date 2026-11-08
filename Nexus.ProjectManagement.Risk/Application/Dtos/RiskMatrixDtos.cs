namespace Nexus.ProjectManagement.RiskManagement.Application.Dtos;

/// <summary>One occupied cell of the probability x impact grid (both scored 1-5).</summary>
public sealed record RiskMatrixCellDto(int ProbabilityScore, int ImpactScore, int Count, IReadOnlyList<Guid> RiskIds);

/// <summary>
/// Only occupied cells are returned; a client renders the full 5x5 grid and treats a missing
/// cell as empty. Callers colour the grid themselves, so no level thresholds are baked in here.
/// </summary>
public sealed record RiskMatrixDto(Guid ProjectId, int TotalRisks, IReadOnlyList<RiskMatrixCellDto> Cells);
