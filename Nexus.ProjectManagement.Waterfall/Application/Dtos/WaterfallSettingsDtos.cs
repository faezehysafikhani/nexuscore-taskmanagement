using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

/// <summary>How a project weighs and plans its activities. IsDefault = the project has not set any of it.</summary>
public sealed record WaterfallSettingsDto(
    Guid ProjectId, WeightingMode Weighting, int TimeFactor, int CostFactor, int ComplexityFactor,
    PlannedProgressMode PlannedProgress, bool IsDefault);

/// <summary>The three factors only count for <see cref="WeightingMode.Combined"/>, where they must add up to 100.</summary>
public sealed record SaveWaterfallSettingsRequest(
    Guid TenantId, Guid ProjectId, WeightingMode Weighting, int TimeFactor, int CostFactor, int ComplexityFactor,
    PlannedProgressMode PlannedProgress);

public sealed record WaterfallSettingsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);

/// <summary>Everything a screen needs to show a project's plan: the activities as stored, their dependencies, the calculated
/// schedule (dates, float, critical path, weights, progress) and the project's weighting / planning settings.</summary>
public sealed record ProjectPlanDto(
    Guid ProjectId, WaterfallSettingsDto Settings, IReadOnlyList<ActivityDto> Activities,
    IReadOnlyList<ActivityDependencyDto> Dependencies, ScheduleDto Schedule);

public sealed record ProjectPlansForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
