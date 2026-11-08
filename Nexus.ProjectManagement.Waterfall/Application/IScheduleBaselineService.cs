using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IScheduleBaselineService
{
    Task<Result<IReadOnlyList<ScheduleBaselineDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ScheduleBaselineDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Freezes the project's current calculated schedule as the next baseline.</summary>
    Task<Result<ScheduleBaselineDetailDto>> CreateAsync(CreateScheduleBaselineRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Compares the project's current calculated schedule with a baseline.</summary>
    Task<Result<BaselineVarianceDto>> GetVarianceAsync(Guid id, CancellationToken cancellationToken);
}
