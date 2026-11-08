using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileBoardService
{
    /// <summary>The Kanban board: four columns of cards. Scope it to one sprint with sprintNumber and narrow it by assignee or priority.</summary>
    Task<Result<BoardDto>> GetBoardAsync(Guid projectId, int? sprintNumber, Guid? responsibleUserId, AgileTaskPriority? priority, CancellationToken cancellationToken);

    /// <summary>Tasks in no sprint that are not done, in order.</summary>
    Task<Result<BacklogDto>> GetBacklogAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Drag and drop: put the task in a column at a position, and renumber the columns it touches.</summary>
    Task<Result<AgileTaskDto>> MoveAsync(Guid taskId, MoveAgileTaskRequest request, CancellationToken cancellationToken);
}

public interface IAgileChecklistService
{
    Task<Result<IReadOnlyList<AgileChecklistItemDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result<AgileChecklistItemDto>> AddAsync(Guid taskId, CreateChecklistItemRequest request, CancellationToken cancellationToken);
    Task<Result<AgileChecklistItemDto>> UpdateAsync(Guid taskId, Guid itemId, UpdateChecklistItemRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid taskId, Guid itemId, CancellationToken cancellationToken);
}
