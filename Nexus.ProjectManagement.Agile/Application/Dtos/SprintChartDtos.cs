using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

/// <summary>What a chart counts: story points, or simply the number of tasks.</summary>
public enum ChartMetric
{
    Points = 0,
    Count = 1
}

/// <summary>
/// One day of a sprint's burn chart. Scope is everything in the sprint by the end of that day,
/// Completed the part of it that was Done, Remaining the difference, and IdealRemaining the
/// straight line from the committed scope down to zero on the last day. The first three are null
/// for days that have not happened yet.
/// </summary>
public sealed record BurnPointDto(DateOnly Date, int? Scope, int? Completed, int? Remaining, decimal IdealRemaining);

/// <summary>
/// The data for a burn-up chart (Scope and Completed against the days) or a burn-down chart
/// (Remaining against IdealRemaining) - the same series serves both. CommittedScope is what the
/// sprint held at the end of its first day. Days are UTC dates.
/// </summary>
public sealed record SprintBurnDto(
    Guid SprintId, int SprintNumber, string Name, SprintStatus Status, ChartMetric Metric,
    DateOnly StartDate, DateOnly EndDate,
    int CommittedScope, int CurrentScope, int CurrentCompleted, int CurrentRemaining,
    IReadOnlyList<BurnPointDto> Points, IReadOnlyList<string> Warnings);

public sealed record SprintVelocityDto(
    Guid SprintId, int Number, string Name, DateOnly StartDate, DateOnly EndDate,
    int Committed, int FinalScope, int Completed, int CarriedOver, decimal? CompletionPercent);

/// <summary>Velocity is what a sprint completed; AverageVelocity is the mean over the sprints listed.</summary>
public sealed record VelocityDto(
    Guid ProjectId, ChartMetric Metric, IReadOnlyList<SprintVelocityDto> Sprints,
    decimal AverageVelocity, decimal AverageCommitted);
