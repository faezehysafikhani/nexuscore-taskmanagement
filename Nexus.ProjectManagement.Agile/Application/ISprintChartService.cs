using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface ISprintChartService
{
    Task<Result<SprintBurnDto>> GetBurnAsync(Guid sprintId, ChartMetric metric, CancellationToken cancellationToken);

    /// <summary>The velocity of a project's most recent completed sprints.</summary>
    Task<Result<VelocityDto>> GetVelocityAsync(Guid projectId, int lastSprints, ChartMetric metric, CancellationToken cancellationToken);
}
