using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.MsProject;

/// <summary>A predecessor link as MS Project states it; the lag is in minutes of working time.</summary>
public sealed record MsProjectLink(int PredecessorUid, DependencyType Type, int LagMinutes);

/// <summary>One task of an MS Project file, reduced to what a Waterfall plan can hold. Resources,
/// assignments, constraints, costs and calendars are not carried over.</summary>
public sealed record MsProjectTask(
    int Uid, string Name, int OutlineLevel, bool IsMilestone, bool IsSummary,
    DateOnly? Start, DateOnly? Finish, int? DurationMinutes, decimal PercentComplete,
    IReadOnlyList<MsProjectLink> Links);

public sealed record MsProjectPlan(string? Name, DateOnly? Start, int MinutesPerDay, IReadOnlyList<MsProjectTask> Tasks);
