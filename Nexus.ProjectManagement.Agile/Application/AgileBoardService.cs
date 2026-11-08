using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class AgileBoardService(
    IAgileTaskRepository taskRepository,
    IAgileChecklistRepository checklistRepository,
    IAgileUnitOfWork unitOfWork,
    SprintTracker tracker) : IAgileBoardService
{
    /// <summary>The columns of the board, left to right.</summary>
    public static readonly AgileTaskStatus[] ColumnOrder =
        [AgileTaskStatus.ToDo, AgileTaskStatus.InProgress, AgileTaskStatus.UnderReview, AgileTaskStatus.Done];

    public async Task<Result<BoardDto>> GetBoardAsync(
        Guid projectId, int? sprintNumber, Guid? responsibleUserId, AgileTaskPriority? priority, CancellationToken cancellationToken)
    {
        var tasks = (await taskRepository.ListByProjectAsync(projectId, sprintNumber, cancellationToken))
            .Where(t => responsibleUserId is null || t.ResponsibleUserId == responsibleUserId)
            .Where(t => priority is null || t.Priority == priority)
            .ToList();
        var cards = await CardsAsync(tasks, cancellationToken);

        var columns = ColumnOrder.Select(status =>
        {
            var inColumn = cards.Where(c => c.Status == status).ToList();
            return new BoardColumnDto(status, inColumn.Count, inColumn.Sum(c => c.StoryPoints ?? 0), inColumn);
        }).ToList();

        return Result.Success(new BoardDto(projectId, sprintNumber, columns));
    }

    public async Task<Result<BacklogDto>> GetBacklogAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var tasks = (await taskRepository.ListByProjectAsync(projectId, null, cancellationToken))
            .Where(t => t.SprintNumber is null && t.Status != AgileTaskStatus.Done)
            .ToList();
        var cards = await CardsAsync(tasks, cancellationToken);

        return Result.Success(new BacklogDto(projectId, cards, cards.Sum(c => c.StoryPoints ?? 0), cards.Count(c => c.StoryPoints is null)));
    }

    public async Task<Result<AgileTaskDto>> MoveAsync(Guid taskId, MoveAgileTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."));
        }

        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<AgileTaskDto>(Error.Validation("Unknown status."));
        }

        // Dropping a card onto itself is what a drag that ends where it began looks like: nothing to do.
        if (request.BeforeTaskId == task.Id && request.Status == task.Status)
        {
            return Result.Success(ToDto(task));
        }

        var all = await taskRepository.ListByProjectAsync(task.ProjectId, null, cancellationToken);
        var oldStatus = task.Status;

        // The target column without the card being moved.
        var target = Ordered(all.Where(t => t.Status == request.Status && t.Id != task.Id)).ToList();
        var index = target.Count;
        if (request.BeforeTaskId is { } beforeId)
        {
            index = target.FindIndex(t => t.Id == beforeId);
            if (index < 0)
            {
                return Result.Failure<AgileTaskDto>(Error.Validation("BeforeTaskId must be a card in the column the task is moving to."));
            }
        }

        var before = TaskSprintState.Of(task);
        task.ChangeStatus(request.Status);
        target.Insert(index, task);
        Renumber(target);

        if (oldStatus != request.Status)
        {
            // Close the gap the card left behind.
            Renumber(Ordered(all.Where(t => t.Status == oldStatus && t.Id != task.Id)).ToList());
        }

        await tracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(task));
    }

    private static AgileTaskDto ToDto(AgileTask task) => new(
        task.Id, task.TenantId, task.ProjectId, task.Title, task.Description, task.Status,
        task.ResponsibleUserId, task.ApproverUserId, task.DueDate, task.Priority, task.SprintNumber, task.ApprovalStatus,
        task.StoryPoints, task.Rank);

    private static IEnumerable<AgileTask> Ordered(IEnumerable<AgileTask> tasks) =>
        tasks.OrderBy(t => t.Rank).ThenBy(t => t.Title, StringComparer.Ordinal).ThenBy(t => t.Id);

    private static void Renumber(IReadOnlyList<AgileTask> column)
    {
        for (var position = 0; position < column.Count; position++)
        {
            if (column[position].Rank != position)
            {
                column[position].SetRank(position);
            }
        }
    }

    private async Task<List<BoardCardDto>> CardsAsync(IReadOnlyList<AgileTask> tasks, CancellationToken cancellationToken)
    {
        var items = await checklistRepository.ListByTasksAsync(tasks.Select(t => t.Id).ToList(), cancellationToken);
        var byTask = items.GroupBy(i => i.TaskId).ToDictionary(g => g.Key, g => (Done: g.Count(i => i.IsDone), Total: g.Count()));

        // Board order first (LINQ's OrderBy is stable, so each column keeps its rank order).
        return Ordered(tasks).OrderBy(t => Array.IndexOf(ColumnOrder, t.Status))
            .Select(t =>
            {
                var counts = byTask.GetValueOrDefault(t.Id);
                return new BoardCardDto(
                    t.Id, t.Title, t.Status, t.Priority, t.ResponsibleUserId, t.DueDate, t.SprintNumber, t.StoryPoints, t.Rank,
                    counts.Done, counts.Total, t.ApprovalStatus);
            })
            .ToList();
    }
}

public sealed class AgileChecklistService(
    IAgileTaskRepository taskRepository,
    IAgileChecklistRepository repository,
    IAgileUnitOfWork unitOfWork) : IAgileChecklistService
{
    /// <summary>A checklist is a few small steps; a card with hundreds is a task that should be split.</summary>
    public const int MaxItemsPerTask = 100;

    public async Task<Result<IReadOnlyList<AgileChecklistItemDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (await taskRepository.GetByIdAsync(taskId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<AgileChecklistItemDto>>(Error.NotFound("Agile task not found."));
        }

        var items = await repository.ListByTaskAsync(taskId, cancellationToken);
        return Result.Success<IReadOnlyList<AgileChecklistItemDto>>(items.OrderBy(i => i.Order).Select(ToDto).ToList());
    }

    public async Task<Result<AgileChecklistItemDto>> AddAsync(Guid taskId, CreateChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileChecklistItemDto>(Error.NotFound("Agile task not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Result.Failure<AgileChecklistItemDto>(Error.Validation("Text is required."));
        }

        var existing = await repository.ListByTaskAsync(taskId, cancellationToken);
        if (existing.Count >= MaxItemsPerTask)
        {
            return Result.Failure<AgileChecklistItemDto>(Error.Conflict($"A checklist holds at most {MaxItemsPerTask} items."));
        }

        var item = new AgileChecklistItem(Guid.NewGuid(), task.TenantId, taskId, request.Text, existing.Count == 0 ? 0 : existing.Max(i => i.Order) + 1);
        await repository.AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<AgileChecklistItemDto>> UpdateAsync(Guid taskId, Guid itemId, UpdateChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(itemId, cancellationToken);
        if (item is null || item.TaskId != taskId)
        {
            return Result.Failure<AgileChecklistItemDto>(Error.NotFound("Checklist item not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Result.Failure<AgileChecklistItemDto>(Error.Validation("Text is required."));
        }

        item.Update(request.Text, request.IsDone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result> DeleteAsync(Guid taskId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(itemId, cancellationToken);
        if (item is null || item.TaskId != taskId)
        {
            return Result.Failure(Error.NotFound("Checklist item not found."));
        }

        await repository.RemoveAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static AgileChecklistItemDto ToDto(AgileChecklistItem item) => new(item.Id, item.TaskId, item.Text, item.IsDone, item.Order);
}
