using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface ISprintChartService
{
    Task<Result<SprintBurnDto>> GetBurnAsync(Guid sprintId, ChartMetric metric, CancellationToken cancellationToken);

    /// <summary>The whole project sprint by sprint: what each sprint holds and how much of it is Done, with running totals.
    /// A task without a sprint counts in sprint 1. An implementation written before this existed reports it as unsupported.</summary>
    Task<Result<ProjectSprintProgressDto>> GetProjectProgressAsync(Guid projectId, ChartMetric metric, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This sprint chart service cannot chart a whole project.");

    /// <summary>The velocity of a project's most recent completed sprints.</summary>
    Task<Result<VelocityDto>> GetVelocityAsync(Guid projectId, int lastSprints, ChartMetric metric, CancellationToken cancellationToken);
}
