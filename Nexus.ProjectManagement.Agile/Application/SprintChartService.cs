using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class SprintChartService(
    ISprintRepository sprintRepository,
    ISprintEventRepository eventRepository,
    TimeProvider timeProvider,
    IAgileTaskRepository? taskRepository = null) : ISprintChartService
{
    public const int DefaultVelocitySprints = 5;
    public const int MaxVelocitySprints = 50;

    public async Task<Result<SprintBurnDto>> GetBurnAsync(Guid sprintId, ChartMetric metric, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(sprintId, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<SprintBurnDto>(Error.NotFound("Sprint not found."));
        }

        var events = await eventRepository.ListBySprintAsync(sprint.ProjectId, sprint.Number, cancellationToken);
        return SprintCharts.Burn(sprint, events, metric, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
    }

    public async Task<Result<ProjectSprintProgressDto>> GetProjectProgressAsync(Guid projectId, ChartMetric metric, CancellationToken cancellationToken)
    {
        if (taskRepository is null)
        {
            return Result.Failure<ProjectSprintProgressDto>(Error.Conflict("Agile tasks are not available."));
        }

        var sprints = await sprintRepository.ListByProjectAsync(projectId, cancellationToken);
        var tasks = await taskRepository.ListByProjectAsync(projectId, null, cancellationToken);
        int Weight(Domain.AgileTask task) => metric == ChartMetric.Points ? task.StoryPoints ?? 0 : 1;
        int SprintOf(Domain.AgileTask task) => task.SprintNumber is > 0 ? task.SprintNumber.Value : 1;

        var count = Math.Max(1, Math.Max(sprints.Count == 0 ? 0 : sprints.Max(s => s.Number), tasks.Count == 0 ? 0 : tasks.Max(SprintOf)));
        var points = new List<SprintProgressPointDto>();
        int total = 0, completed = 0;
        for (var number = 1; number <= count; number++)
        {
            var inSprint = tasks.Where(t => SprintOf(t) == number).ToList();
            var sprintTotal = inSprint.Sum(Weight);
            var sprintCompleted = inSprint.Where(t => t.Status == Domain.AgileTaskStatus.Done).Sum(Weight);
            total += sprintTotal;
            completed += sprintCompleted;
            points.Add(new SprintProgressPointDto(
                number, sprints.FirstOrDefault(s => s.Number == number)?.Name ?? $"Sprint {number}",
                sprintTotal, sprintCompleted, total, completed, Math.Max(0, total - completed)));
        }

        return Result.Success(new ProjectSprintProgressDto(projectId, metric, points));
    }

    public async Task<Result<VelocityDto>> GetVelocityAsync(Guid projectId, int lastSprints, ChartMetric metric, CancellationToken cancellationToken)
    {
        if (lastSprints < 1 || lastSprints > MaxVelocitySprints)
        {
            return Result.Failure<VelocityDto>(Error.Validation($"sprints must be between 1 and {MaxVelocitySprints}."));
        }

        var sprints = await sprintRepository.ListByProjectAsync(projectId, cancellationToken);
        var events = await eventRepository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success(SprintCharts.Velocity(projectId, sprints, events, metric, lastSprints));
    }
}
