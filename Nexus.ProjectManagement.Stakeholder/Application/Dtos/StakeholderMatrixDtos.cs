using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;

/// <summary>Standard power/interest engagement quadrants (Mendelow grid).</summary>
public enum StakeholderQuadrant
{
    /// <summary>Low-to-medium power, low-to-medium interest.</summary>
    Monitor,
    /// <summary>Low-to-medium power, high interest.</summary>
    KeepInformed,
    /// <summary>High power, low-to-medium interest.</summary>
    KeepSatisfied,
    /// <summary>High power, high interest.</summary>
    ManageClosely
}

public sealed record StakeholderMatrixItemDto(Guid Id, string Name, bool IsInternal);

/// <summary>
/// Only occupied power x interest cells are returned. The quadrant treats "High" as the high
/// side of each axis and Medium/Low as the low side; a client that wants a different split can
/// ignore Quadrant and place the cells itself.
/// </summary>
public sealed record StakeholderMatrixCellDto(
    PowerLevel Power, InterestLevel Interest, StakeholderQuadrant Quadrant,
    IReadOnlyList<StakeholderMatrixItemDto> Stakeholders);

public sealed record StakeholderMatrixDto(Guid ProjectId, int TotalStakeholders, IReadOnlyList<StakeholderMatrixCellDto> Cells);
