using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface ISprintService
{
    Task<Result<IReadOnlyList<SprintDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>The sprints of several projects at once. A service written before this existed reports it as unsupported.</summary>
    Task<Result<IReadOnlyList<SprintDto>>> ListForProjectsAsync(ListAgileForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This sprint service cannot list the sprints of several projects.");

    /// <summary>Gives the project exactly sprints 1..Count: missing ones are created (planned); surplus ones are removed from the end
    /// while they are planned and hold no tasks. A service written before this existed reports it as unsupported.</summary>
    Task<Result<IReadOnlyList<SprintDto>>> SetCountAsync(SetSprintCountRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This sprint service cannot set a sprint count.");

    Task<Result<SprintDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<SprintDto>> CreateAsync(CreateSprintRequest request, CancellationToken cancellationToken);
    Task<Result<SprintDto>> UpdateAsync(Guid id, UpdateSprintRequest request, CancellationToken cancellationToken);

    /// <summary>Planned -> Active. Needs both dates, and no other sprint of the project may be active.</summary>
    Task<Result<SprintDto>> StartAsync(Guid id, StartSprintRequest request, CancellationToken cancellationToken);

    /// <summary>Active -> Completed. Unfinished tasks move to another sprint or back to the backlog; finished ones stay.</summary>
    Task<Result<CompleteSprintResultDto>> CompleteAsync(Guid id, CompleteSprintRequest request, CancellationToken cancellationToken);

    /// <summary>Only a planned sprint with no tasks can be deleted.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<SprintDto>> AssignTasksAsync(Guid id, AssignSprintTasksRequest request, CancellationToken cancellationToken);
    Task<Result<SprintDto>> RemoveTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken);
}
