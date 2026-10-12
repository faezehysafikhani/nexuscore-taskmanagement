namespace Nexus.ProjectManagement.RiskManagement.Application.Dtos;

public sealed record RiskLevelDefinitionDto(string Axis, string Level, string Range, string Description);

public sealed record RiskMatrixColorDto(string Probability, string Impact, string Color);

/// <summary>What a project says about its risks. IsDefault = it has not set any of it (everything shown is the default).</summary>
public sealed record RiskSettingsDto(
    Guid ProjectId, IReadOnlyList<RiskLevelDefinitionDto> Definitions, IReadOnlyList<RiskMatrixColorDto> MatrixColors, bool IsDefault);

/// <summary>Each part is optional: omitted (null) leaves it as it is; an empty list puts the project back on the default for that part;
/// otherwise it must name every band (six definitions) or every cell (nine colours) exactly once.</summary>
public sealed record SaveRiskSettingsRequest(
    Guid TenantId, Guid ProjectId, IReadOnlyList<RiskLevelDefinitionDto>? Definitions, IReadOnlyList<RiskMatrixColorDto>? MatrixColors);

public sealed record RiskSettingsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
