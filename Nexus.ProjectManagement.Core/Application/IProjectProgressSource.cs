using Nexus.ProjectManagement.Core.Domain;

namespace Nexus.ProjectManagement.Core.Application;

/// <summary>What one module knows about how far a project has got. <c>Planned</c> is null when the module has no schedule to read it from
/// (the project's own dates are used then); the task counts are only given by modules that work with tasks.</summary>
public sealed record ProjectProgressFigures(
    Guid ProjectId, decimal? Planned, decimal Actual,
    int? TotalTasks = null, int? TodoTasks = null, int? DoingTasks = null, int? DoneTasks = null);

/// <summary>
/// How a planning module (Waterfall, Agile, ...) tells Core how far its projects have got, without Core knowing the module: each
/// installed module registers one. A source answers only for the projects it plans (it ignores the others); with no source registered
/// every project is reported from its dates alone.
/// </summary>
public interface IProjectProgressSource
{
    Task<IReadOnlyList<ProjectProgressFigures>> GetProgressAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken);
}
