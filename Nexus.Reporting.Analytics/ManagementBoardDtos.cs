namespace Nexus.Reporting.Analytics;

// The management boards cover the projects that are running (Active or OnHold). Enum values travel as their names so a screen
// or an export shows them as they are.

public sealed record CountByNameDto(string Name, int Count);

// ------------------------------------------------------------------ health

/// <param name="Health">Green, Amber, Red or Unknown - the worse of the schedule and cost indexes, as in the project report.</param>
public sealed record ProjectHealthRowDto(
    Guid ProjectId, string Name, string Code, string Status, Guid? OrganizationUnitId, Guid? ManagerUserId,
    string Health, decimal? PlannedProgress, decimal? ActualProgress, decimal? Spi, decimal? Cpi,
    int? ForecastDelayDays, bool IsOverdue);

/// <summary>Counts always list Green, Amber, Red and Unknown (a status nobody is in shows 0). Rows are the worst first.</summary>
public sealed record HealthBoardDto(DateOnly AsOf, int ProjectCount, IReadOnlyList<CountByNameDto> Counts, IReadOnlyList<ProjectHealthRowDto> Projects);

// -------------------------------------------------------------- attention

/// <summary>What an attention item is about. SubjectType uses the same words as the approval subjects ("Project", "Risk", "Action").</summary>
public static class AttentionKinds
{
    public const string ProjectOverdue = "ProjectOverdue";
    public const string ProjectRed = "ProjectRed";
    public const string ProjectAtRisk = "ProjectAtRisk";
    public const string RiskWithoutResponsePlan = "RiskWithoutResponsePlan";
    public const string ActionOverdue = "ActionOverdue";
}

public static class AttentionSeverity
{
    public const string High = "High";
    public const string Medium = "Medium";
}

public sealed record AttentionItemDto(
    string Kind, string Severity, string SubjectType, Guid SubjectId, Guid? ProjectId, string? ProjectName,
    string Title, DateOnly? DueDate, int? DaysLate, Guid? OwnerUserId);

/// <param name="Total">Every item found; <c>Items</c> holds at most 500 of them, the most severe and the most late first.</param>
/// <param name="RisksIncluded">False when risk management is not installed, so no risk items were looked for.</param>
public sealed record AttentionBoardDto(
    DateOnly AsOf, int Total, bool Truncated, bool RisksIncluded,
    IReadOnlyList<CountByNameDto> CountsByKind, IReadOnlyList<AttentionItemDto> Items);

// ------------------------------------------------------------------ risks

/// <summary>High, Medium and Low are the risk matrix colours red, yellow and green of the project's own matrix.
/// Only approved risks count; a draft or rejected one is not yet an official risk.</summary>
public sealed record RiskOverviewProjectRowDto(Guid ProjectId, string Name, string Code, int Total, int High, int Medium, int Low, int OpenHigh);

public sealed record TopRiskDto(
    Guid RiskId, Guid ProjectId, string ProjectName, string Description, int ProbabilityScore, int ImpactScore, int Rpn,
    string Level, string ResponseStatus, Guid? RiskOwnerUserId, bool HasResponsePlan);

/// <param name="OpenHigh">High risks whose response is not completed.</param>
/// <param name="TopRisks">The ten open high risks with the largest RPN.</param>
public sealed record RiskOverviewDto(
    DateOnly AsOf, int Total, int High, int Medium, int Low, int OpenHigh, int OpenHighWithoutResponsePlan,
    IReadOnlyList<CountByNameDto> ByResponseStatus, IReadOnlyList<RiskOverviewProjectRowDto> Projects, IReadOnlyList<TopRiskDto> TopRisks);

// ------------------------------------------------------------------- KPIs

public sealed record KpiPointDto(DateOnly MeasuredOn, decimal Value);

/// <summary>A KPI against its target. AchievementPercent = latest value / target x 100 (a higher value is better), null without a target above
/// zero or without a value. Rating is the same Excellent / Good / NeedsImprovement scale as project performance, on the achievement capped at 100.</summary>
public sealed record KpiStatusDto(
    Guid KpiId, Guid ProjectId, string ProjectName, string ProjectCode, Guid DeliverableId, string Type, string Description, string? Formula,
    decimal? TargetValue, decimal? LatestValue, DateOnly? LatestMeasuredOn, int MeasurementCount,
    decimal? AchievementPercent, string? Rating, IReadOnlyList<KpiPointDto> Trend);

public sealed record KpiOverviewDto(
    DateOnly AsOf, int KpiCount, int WithValues, int WithoutValues, int Excellent, int Good, int NeedsImprovement,
    decimal? AverageAchievement, IReadOnlyList<KpiStatusDto> Kpis);
