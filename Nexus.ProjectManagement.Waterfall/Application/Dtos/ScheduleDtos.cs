using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ScheduledActivityDto(
    Guid Id, Guid? ParentActivityId, string Name, bool IsSummary, bool IsMilestone,
    DateOnly Start, DateOnly Finish, int DurationDays,
    DateOnly LateStart, DateOnly LateFinish, int TotalFloatDays, bool IsCritical,
    decimal PlannedProgress, decimal ActualProgress, bool UsedDefaultDuration,
    string WbsCode = "", int Level = 1, decimal Weight = 0, decimal Cost = 0, decimal ManHours = 0);

/// <summary>
/// A project's calculated schedule. Durations and float are in working days of the project's work
/// calendar (UsesWorkCalendar), or plain calendar days when it has none. Progress figures are the
/// weighted roll-up of the per-activity progress. Applied says whether the dates were also
/// written back to the activities. WbsCode ("1", "1.2", ...), Level (top level = 1), the effective Weight of
/// every activity (a summary's is the sum of its children's) and the rolled-up Cost and ManHours are worked
/// out here so no client has to; Weighting and PlannedProgressMode say how the project weighs and plans.
/// </summary>
public sealed record ScheduleDto(
    Guid ProjectId, DateOnly ProjectStart, DateOnly ProjectFinish, int ProjectDurationDays,
    bool UsesWorkCalendar, decimal PlannedProgress, decimal ActualProgress,
    IReadOnlyList<Guid> CriticalPath, IReadOnlyList<ScheduledActivityDto> Activities,
    IReadOnlyList<string> Warnings, bool Applied,
    WeightingMode Weighting = WeightingMode.Manual, PlannedProgressMode PlannedProgressMode = PlannedProgressMode.Stored);
