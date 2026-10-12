using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public sealed class ProjectProgressService(
    IProjectRepository repository,
    IEnumerable<IProjectProgressSource> sources,
    TimeProvider timeProvider) : IProjectProgressService
{
    public async Task<Result<IReadOnlyList<ProjectProgressDto>>> GetAsync(Guid tenantId, ProjectProgressRequest request, CancellationToken cancellationToken)
    {
        var projects = new List<Project>();
        foreach (var id in request.ProjectIds.Distinct())
        {
            var project = await repository.GetByIdAsync(id, cancellationToken);
            if (project is not null && project.TenantId == tenantId)
            {
                projects.Add(project);
            }
        }

        var figures = new Dictionary<Guid, ProjectProgressFigures>();
        if (projects.Count > 0)
        {
            foreach (var source in sources)
            {
                foreach (var item in await source.GetProgressAsync(projects, cancellationToken))
                {
                    figures[item.ProjectId] = item;
                }
            }
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var result = projects.Select(project =>
        {
            figures.TryGetValue(project.Id, out var known);
            var planned = Math.Round(known?.Planned ?? ElapsedShare(project, today), 2);
            var actual = Math.Round(known?.Actual ?? 0m, 2);
            var performance = planned > 0 ? Math.Round(actual / planned * 100m, 2) : actual > 0 ? 100m : 0m;
            return new ProjectProgressDto(
                project.Id, project.Type, planned, actual, performance,
                known?.TotalTasks, known?.TodoTasks, known?.DoingTasks, known?.DoneTasks);
        }).ToList();

        return Result.Success<IReadOnlyList<ProjectProgressDto>>(result);
    }

    /// <summary>How much of the project's time has gone by, 0-100, from its start and end dates alone.</summary>
    private static decimal ElapsedShare(Project project, DateOnly today)
    {
        if (project.StartDate is not { } start || project.EndDate is not { } end)
        {
            return 0m;
        }

        if (today >= end)
        {
            return 100m;
        }

        if (today <= start || end <= start)
        {
            return 0m;
        }

        return Math.Clamp((decimal)(today.DayNumber - start.DayNumber) / (end.DayNumber - start.DayNumber) * 100m, 0m, 100m);
    }
}
