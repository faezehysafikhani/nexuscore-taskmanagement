namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ProgressSnapshotDto(
    Guid Id, Guid TenantId, Guid ProjectId, DateOnly SnapshotDate,
    decimal PlannedProgress, decimal ActualProgress, string? Note, Guid? CreatedByUserId);

/// <summary>SnapshotDate defaults to today when omitted.</summary>
public sealed record CreateProgressSnapshotRequest(Guid TenantId, Guid ProjectId, DateOnly? SnapshotDate, string? Note);

public sealed record CurvePointDto(DateOnly Date, decimal PlannedProgress);

public sealed record ActualPointDto(DateOnly Date, decimal ActualProgress, decimal PlannedProgress);

/// <summary>
/// The data behind a project's S-curve. Planned is calculated from the schedule alone, sampled
/// every StepDays calendar days from the project's start to its finish (the finish date is always
/// included). Actual is the project's snapshots, oldest first - the only recorded history of
/// progress. Current* is where the project stands today. SchedulePerformanceIndex is
/// actual / planned today (null while nothing should be done yet): below 1 is behind schedule.
/// </summary>
public sealed record SCurveDto(
    Guid ProjectId, DateOnly ProjectStart, DateOnly ProjectFinish, int StepDays, DateOnly Today,
    IReadOnlyList<CurvePointDto> Planned, IReadOnlyList<ActualPointDto> Actual,
    decimal CurrentPlannedProgress, decimal CurrentActualProgress, decimal ProgressVariance,
    decimal? SchedulePerformanceIndex);
