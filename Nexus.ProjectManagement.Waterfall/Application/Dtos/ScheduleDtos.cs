namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ScheduledActivityDto(
    Guid Id, Guid? ParentActivityId, string Name, bool IsSummary, bool IsMilestone,
    DateOnly Start, DateOnly Finish, int DurationDays,
    DateOnly LateStart, DateOnly LateFinish, int TotalFloatDays, bool IsCritical,
    decimal PlannedProgress, decimal ActualProgress, bool UsedDefaultDuration);

/// <summary>
/// A project's calculated schedule. Durations and float are in working days of the project's work
/// calendar (UsesWorkCalendar), or plain calendar days when it has none. Progress figures are the
/// weighted roll-up of the stored per-activity progress. Applied says whether the dates were also
/// written back to the activities.
/// </summary>
public sealed record ScheduleDto(
    Guid ProjectId, DateOnly ProjectStart, DateOnly ProjectFinish, int ProjectDurationDays,
    bool UsesWorkCalendar, decimal PlannedProgress, decimal ActualProgress,
    IReadOnlyList<Guid> CriticalPath, IReadOnlyList<ScheduledActivityDto> Activities,
    IReadOnlyList<string> Warnings, bool Applied);
