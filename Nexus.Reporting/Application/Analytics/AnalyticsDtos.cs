namespace Nexus.Reporting.Application.Analytics;

/// <summary>One project's schedule/cost performance. Money is in the project's own unit (the
/// budget is Project.Cost; actual cost is the approved invoices of its contracts, in rials).
/// Every ...Percent and index is null when it cannot be worked out.</summary>
public sealed record ProjectPerformanceDto(
    Guid ProjectId, string Name, string Code, string Status, Guid? OrganizationUnitId, Guid? ManagerUserId,
    DateOnly? StartDate, DateOnly? EndDate, DateOnly AsOf,
    decimal? PlannedProgress, decimal? ActualProgress, DateOnly? ProgressDate,
    decimal? BudgetAtCompletion, decimal? PlannedValue, decimal? EarnedValue, decimal? ScheduleVariance, decimal? Spi,
    decimal? ActualCost, decimal? CostVariance, decimal? Cpi,
    decimal? EstimateAtCompletion, decimal? EstimateToComplete, decimal? VarianceAtCompletion,
    DateOnly? ForecastEnd, int? ForecastDelayDays, bool IsOverdue, HealthStatus Health,
    decimal? ContractedAmount, decimal? InvoicedAmount, decimal? PaidAmount, bool ContractsAvailable, bool ProgressAvailable);

public sealed record StatusCountDto(string Status, int Count);

/// <summary>A unit of the organisation chart with the projects of its whole subtree rolled up into it.</summary>
public sealed record UnitPerformanceDto(
    Guid? UnitId, string UnitName, string? UnitCode, int Level,
    int ProjectCount, int RunningProjects, int CompletedProjects, int OverdueProjects,
    decimal TotalBudget, int ProjectsWithProgress, decimal? AveragePlannedProgress, decimal? AverageActualProgress, decimal? AverageDeviation,
    int OnTrack, int AtRisk, int Behind);

public sealed record UnitPerformanceReportDto(
    int Level, int MaxLevel, DateOnly AsOf, IReadOnlyList<UnitPerformanceDto> Units);

/// <summary>Rows are units at <c>Level</c>; columns are every project status, so a status with no
/// project still shows as a zero column.</summary>
public sealed record UnitStatusMatrixDto(
    int Level, IReadOnlyList<string> Statuses, IReadOnlyList<UnitStatusRowDto> Rows, IReadOnlyList<int> ColumnTotals, int Total);

public sealed record UnitStatusRowDto(Guid? UnitId, string UnitName, string? UnitCode, IReadOnlyList<int> Counts, int Total);

/// <summary>How the projects a person manages are doing. The classification counts come from each
/// project's latest progress update.</summary>
public sealed record ProjectManagerEvaluationDto(
    Guid? ManagerUserId, int ProjectCount, int RunningProjects, int CompletedProjects, int OverdueProjects,
    decimal TotalBudget, int ProjectsWithProgress, decimal? AveragePlannedProgress, decimal? AverageActualProgress, decimal? AverageDeviation,
    int OnTrack, int AtRisk, int Behind, decimal? OnTrackShare, int OpenActions);

public sealed record ProjectManagerEvaluationReportDto(DateOnly AsOf, IReadOnlyList<ProjectManagerEvaluationDto> Managers);

public sealed record AlignmentStrategyColumnDto(
    Guid StrategyId, string Name, Guid? ParentStrategyId, int Depth, decimal Weight,
    int DirectProjects, int ProjectsIncludingChildren, int HighAlignedProjects, decimal? AveragePercentage);

public sealed record AlignmentCellDto(string Level, decimal? Percentage);

public sealed record AlignmentProjectRowDto(
    Guid ProjectId, string Name, string Code, IReadOnlyList<AlignmentCellDto> Cells,
    int AlignedStrategies, decimal? AveragePercentage, decimal? WeightedPercentage);

/// <summary>The complete Project x Strategy matrix: every project is a row and every strategy a
/// column, whether or not a link exists (its cell is level "None"). <c>Cells</c> follow the order of
/// <c>Strategies</c>. A strategy no project is aligned to, and a project aligned to no strategy, are listed
/// separately - the gaps are the point of the report.</summary>
public sealed record StrategyAlignmentMatrixDto(
    IReadOnlyList<AlignmentStrategyColumnDto> Strategies, IReadOnlyList<AlignmentProjectRowDto> Projects,
    IReadOnlyList<Guid> UncoveredStrategyIds, IReadOnlyList<Guid> UnalignedProjectIds);
