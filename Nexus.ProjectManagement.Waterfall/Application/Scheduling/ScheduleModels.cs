using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>What the calculator needs to know about one activity - nothing about storage.</summary>
public sealed record ScheduleActivityInput(
    Guid Id, Guid? ParentId, string Name, bool IsMilestone, int? DurationDays,
    DateOnly? StartDate, DateOnly? EndDate, decimal Weight, decimal PlannedProgress, decimal ActualProgress);

public sealed record ScheduleLinkInput(Guid PredecessorId, Guid SuccessorId, DependencyType Type, int LagDays);

/// <summary>
/// One activity's calculated place in the plan. A leaf occupies the working-day span
/// [StartIndex, FinishIndex) on the schedule's axis (zero-length for a milestone); a summary
/// spans its children. Float is in working days; critical means no float.
/// </summary>
public sealed record ScheduledActivity(
    Guid Id, Guid? ParentId, string Name, bool IsSummary, bool IsMilestone,
    DateOnly Start, DateOnly Finish, int DurationDays,
    DateOnly LateStart, DateOnly LateFinish, int TotalFloatDays, bool IsCritical,
    decimal PlannedProgress, decimal ActualProgress,
    int StartIndex, int FinishIndex, bool UsedDefaultDuration);

public sealed record ScheduleResult(
    DateOnly ProjectStart, DateOnly ProjectFinish, int ProjectDurationDays,
    IReadOnlyList<ScheduledActivity> Activities, IReadOnlyList<Guid> CriticalPath,
    decimal PlannedProgress, decimal ActualProgress, WorkingDayAxis Axis);
