namespace Nexus.Reporting.Application.Dtos;

public sealed record CountByKey(string Key, int Count);

public sealed record DashboardSummaryDto(
    int ProjectCount,
    int RunningProjectCount,
    int ActionCount,
    int RunningActionCount,
    IReadOnlyList<CountByKey> ProjectsByStatus,
    IReadOnlyList<CountByKey> ProjectsByOrganizationUnit,
    IReadOnlyList<CountByKey> ProjectsByManager);

public sealed record MyDashboardDto(int MyRunningProjectCount, int MyRunningActionCount, IReadOnlyList<Guid> MyProjectIds, IReadOnlyList<Guid> MyActionIds);

/// <summary>Deviation/PerformanceClassification are read directly from Progress's own latest
/// update for this project - Reporting never recomputes them, per rule: "Business Logic اصلی
/// را داخل Reporting duplicate نکن". Null fields mean either Progress Management isn't
/// installed or this project has no progress updates yet.</summary>
public sealed record ProjectDashboardDto(
    Guid ProjectId, string Name, string Status,
    decimal? LatestPlannedProgress, decimal? LatestActualProgress,
    decimal? Deviation, string? PerformanceClassification);
