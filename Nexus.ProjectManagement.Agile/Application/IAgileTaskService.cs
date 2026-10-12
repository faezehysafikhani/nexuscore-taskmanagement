using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileTaskService
{
    Task<Result<IReadOnlyList<AgileTaskDto>>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken);
    /// <summary>The tasks of several projects at once. A service written before this existed reports it as unsupported.</summary>
    Task<Result<IReadOnlyList<AgileTaskDto>>> ListForProjectsAsync(ListAgileForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This task service cannot list the tasks of several projects.");

    Task<Result<AgileTaskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> CreateAsync(CreateAgileTaskRequest request, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> UpdateAsync(Guid id, UpdateAgileTaskRequest request, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> ChangeStatusAsync(Guid id, ChangeAgileTaskStatusRequest request, CancellationToken cancellationToken);
    /// <summary>Sets or (with null) clears the story-point estimate - the one way to un-estimate a task.</summary>
    Task<Result<AgileTaskDto>> SetStoryPointsAsync(Guid id, SetStoryPointsRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
