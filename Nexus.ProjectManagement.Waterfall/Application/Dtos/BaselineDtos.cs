using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ScheduleBaselineDto(
    Guid Id, Guid TenantId, Guid ProjectId, int Number, string Name, string? Note,
    DateOnly ProjectStart, DateOnly ProjectFinish, int ActivityCount, DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId,
    BaselineKind Kind = BaselineKind.Baseline, decimal PlannedProgress = 0, decimal ActualProgress = 0);

public sealed record BaselineActivityDto(
    Guid ActivityId, Guid? ParentActivityId, string Name, bool IsSummary, bool IsMilestone,
    DateOnly StartDate, DateOnly EndDate, int DurationDays,
    decimal Weight = 0, decimal PlannedProgress = 0, decimal ActualProgress = 0, Guid? ResponsibleUserId = null, string WbsCode = "");

public sealed record ScheduleBaselineDetailDto(ScheduleBaselineDto Baseline, IReadOnlyList<BaselineActivityDto> Activities);

/// <summary>Kind says what the copy is for (default: a baseline to compare against); the activities' weights, progress and owners are
/// kept in it either way.</summary>
public sealed record CreateScheduleBaselineRequest(Guid TenantId, Guid ProjectId, string Name, string? Note, BaselineKind Kind = BaselineKind.Baseline);

/// <summary>How an activity stands against the baseline, judged by its finish.</summary>
public enum VarianceStatus
{
    /// <summary>Finishes on the baseline date.</summary>
    OnTrack = 0,
    /// <summary>Finishes after the baseline date.</summary>
    Late = 1,
    /// <summary>Finishes before the baseline date.</summary>
    Early = 2,
    /// <summary>Exists now but was not in the baseline.</summary>
    Added = 3,
    /// <summary>Was in the baseline but no longer exists.</summary>
    Removed = 4
}

/// <summary>Variances are current minus baseline, in calendar days (positive = later/longer).
/// Null where one side does not exist.</summary>
public sealed record ActivityVarianceDto(
    Guid ActivityId, string Name, bool IsSummary, bool IsMilestone, VarianceStatus Status,
    DateOnly? BaselineStart, DateOnly? BaselineFinish, int? BaselineDurationDays,
    DateOnly? CurrentStart, DateOnly? CurrentFinish, int? CurrentDurationDays,
    int? StartVarianceDays, int? FinishVarianceDays, int? DurationVarianceDays);

public sealed record BaselineVarianceDto(
    Guid BaselineId, int Number, string Name, Guid ProjectId,
    DateOnly BaselineProjectStart, DateOnly BaselineProjectFinish,
    DateOnly CurrentProjectStart, DateOnly CurrentProjectFinish,
    int ProjectStartVarianceDays, int ProjectFinishVarianceDays,
    int OnTrackCount, int LateCount, int EarlyCount, int AddedCount, int RemovedCount,
    IReadOnlyList<ActivityVarianceDto> Activities);
