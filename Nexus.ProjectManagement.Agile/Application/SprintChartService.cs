using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class SprintChartService(
    ISprintRepository sprintRepository,
    ISprintEventRepository eventRepository,
    TimeProvider timeProvider) : ISprintChartService
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
