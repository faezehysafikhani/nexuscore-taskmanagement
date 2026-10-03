using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IScheduleService
{
    /// <summary>Calculates the project's schedule (dates, float, critical path, rolled-up progress) without changing anything.</summary>
    Task<Result<ScheduleDto>> GetScheduleAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Calculates the schedule and writes the resulting start, end and duration back to the activities.</summary>
    Task<Result<ScheduleDto>> ApplyScheduleAsync(Guid projectId, CancellationToken cancellationToken);
}
