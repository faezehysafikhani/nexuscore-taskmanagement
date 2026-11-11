using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// What stands between a task and "done" (phase 3c): the tasks it waits for, a free-text blocker, acceptance
/// criteria, and the per-person limit of tasks in progress. The rules that refuse a status change live in
/// <see cref="TaskPlanningRules"/> and are called by TaskService itself, so no path around them exists.
/// </summary>
public sealed class TaskPlanningService(
    ITaskPlanningRepository repository,
    ITaskRepository taskRepository,
    ITaskService tasks,
    ITaskManagementUnitOfWork unitOfWork,
    ITaskAccessScope access,
    ITaskActivityService activity,
    ICurrentUserContext currentUser) : ITaskPlanningService
{
    public const int MaxCriteria = 50;
    public const int MaxBulkSubTasks = 100;

    private async Task<(TaskItem? Task, Result? Failure)> LoadAsync(Guid taskId, bool needManage, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return (null, Result.Failure(Error.Unauthorized()));
        }

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null)
        {
            return (null, Result.Failure(Error.NotFound("Task not found.")));
        }

        var allowed = access.CanManage(task) || (!needManage && currentUser.UserId is { } who && task.IsResponsible(who));
        return allowed ? (task, null) : (null, Result.Failure(Error.Forbidden("Only the task owner or a task manager can change this.")));
    }

    public async Task<Result<TaskPlanningDto>> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskPlanningDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        return task is null ? Result.Failure<TaskPlanningDto>(Error.NotFound("Task not found.")) : Result.Success(await BuildAsync(task, cancellationToken));
    }

    private async Task<TaskPlanningDto> BuildAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var blockedBy = await repository.ListDependenciesAsync(task.TenantId, task.Id, cancellationToken);
        var blocking = await repository.ListDependentsAsync(task.TenantId, task.Id, cancellationToken);
        var briefs = await repository.BriefsAsync(task.TenantId, blockedBy.Select(d => d.BlockedByTaskId).Concat(blocking.Select(d => d.TaskId)).Distinct().ToList(), cancellationToken);
        var byId = briefs.ToDictionary(b => b.Id);
        var criteria = await repository.ListCriteriaAsync(task.TenantId, task.Id, cancellationToken);

        var by = blockedBy.Select(d => byId.GetValueOrDefault(d.BlockedByTaskId)).OfType<TaskBriefDto>().ToList();
        var canComplete = !by.Any(b => !b.IsDone) && criteria.All(c => c.IsMet);
        return new TaskPlanningDto(
            task.BlockedReason,
            by,
            blocking.Select(d => byId.GetValueOrDefault(d.TaskId)).OfType<TaskBriefDto>().ToList(),
            criteria.Select(ToDto).ToList(),
            canComplete);
    }

    private static CriterionDto ToDto(TaskCriterion c) => new(c.Id, c.Text, c.IsMet, c.SortOrder);

    // --- dependencies ---

    public async Task<Result<TaskPlanningDto>> AddDependencyAsync(Guid taskId, AddTaskDependencyRequest request, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: true, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        if (request.BlockedByTaskId == taskId)
        {
            return Result.Failure<TaskPlanningDto>(Error.Validation("A task cannot wait for itself."));
        }

        var blocker = await taskRepository.GetByIdAsync(task.TenantId, request.BlockedByTaskId, cancellationToken);
        if (blocker is null)
        {
            return Result.Failure<TaskPlanningDto>(Error.NotFound("The task to wait for was not found."));
        }

        if (await repository.GetDependencyAsync(task.TenantId, taskId, blocker.Id, cancellationToken) is not null)
        {
            return Result.Failure<TaskPlanningDto>(Error.Conflict("This dependency already exists."));
        }

        // A cycle: the new blocker already (directly or not) waits for this task.
        if ((await repository.BlockersClosureAsync(task.TenantId, blocker.Id, cancellationToken)).Contains(taskId))
        {
            return Result.Failure<TaskPlanningDto>(Error.Conflict("This would make the tasks wait for each other."));
        }

        await repository.AddDependencyAsync(new TaskDependency(Guid.NewGuid(), task.TenantId, taskId, blocker.Id), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(taskId, "Dependency added", blocker.Title, cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    public async Task<Result<TaskPlanningDto>> RemoveDependencyAsync(Guid taskId, Guid blockedByTaskId, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: true, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        var dependency = await repository.GetDependencyAsync(task.TenantId, taskId, blockedByTaskId, cancellationToken);
        if (dependency is null)
        {
            return Result.Failure<TaskPlanningDto>(Error.NotFound("Dependency not found."));
        }

        repository.RemoveDependency(dependency);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(taskId, "Dependency removed", null, cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    // --- blocker ---

    public async Task<Result<TaskPlanningDto>> SetBlockerAsync(Guid taskId, SetTaskBlockerRequest request, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: false, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        task.SetBlockedReason(request.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(taskId, task.BlockedReason is null ? "Blocker cleared" : "Blocked", task.BlockedReason, cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    // --- criteria ---

    public async Task<Result<TaskPlanningDto>> AddCriterionAsync(Guid taskId, SaveCriterionRequest request, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: true, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        var existing = await repository.ListCriteriaAsync(task.TenantId, taskId, cancellationToken);
        if (existing.Count >= MaxCriteria)
        {
            return Result.Failure<TaskPlanningDto>(Error.Validation($"A task can have at most {MaxCriteria} criteria."));
        }

        await repository.AddCriterionAsync(new TaskCriterion(Guid.NewGuid(), task.TenantId, taskId, request.Text, existing.Count == 0 ? 1 : existing.Max(c => c.SortOrder) + 1), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    public async Task<Result<TaskPlanningDto>> UpdateCriterionAsync(Guid taskId, Guid criterionId, SaveCriterionRequest request, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: true, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        var criterion = await repository.GetCriterionAsync(task.TenantId, criterionId, cancellationToken);
        if (criterion is null || criterion.TaskId != taskId)
        {
            return Result.Failure<TaskPlanningDto>(Error.NotFound("Criterion not found."));
        }

        criterion.Rename(request.Text);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    public async Task<Result<TaskPlanningDto>> SetCriterionMetAsync(Guid taskId, Guid criterionId, SetCriterionMetRequest request, CancellationToken cancellationToken)
    {
        // Ticking a criterion is the work of whoever does the task, not only its owner.
        var (task, failure) = await LoadAsync(taskId, needManage: false, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        var criterion = await repository.GetCriterionAsync(task.TenantId, criterionId, cancellationToken);
        if (criterion is null || criterion.TaskId != taskId)
        {
            return Result.Failure<TaskPlanningDto>(Error.NotFound("Criterion not found."));
        }

        criterion.SetMet(request.Met, currentUser.UserId, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(taskId, request.Met ? "Criterion met" : "Criterion reopened", criterion.Text, cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    public async Task<Result<TaskPlanningDto>> DeleteCriterionAsync(Guid taskId, Guid criterionId, CancellationToken cancellationToken)
    {
        var (task, failure) = await LoadAsync(taskId, needManage: true, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskPlanningDto>(failure!.Error);
        }

        var criterion = await repository.GetCriterionAsync(task.TenantId, criterionId, cancellationToken);
        if (criterion is null || criterion.TaskId != taskId)
        {
            return Result.Failure<TaskPlanningDto>(Error.NotFound("Criterion not found."));
        }

        repository.RemoveCriterion(criterion);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }

    // --- many subtasks at once ---

    public async Task<Result<IReadOnlyList<SubTaskDto>>> AddSubTasksAsync(Guid taskId, BulkSubTasksRequest request, CancellationToken cancellationToken)
    {
        var titles = request.Titles.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (titles.Count == 0 || titles.Count > MaxBulkSubTasks)
        {
            return Result.Failure<IReadOnlyList<SubTaskDto>>(Error.Validation($"Send between 1 and {MaxBulkSubTasks} titles."));
        }

        // Each one goes through the ordinary subtask rules; the first refusal stops the rest (what was made stays).
        var made = new List<SubTaskDto>();
        foreach (var title in titles)
        {
            var created = await tasks.CreateSubTaskAsync(taskId, new CreateSubTaskRequest(title), cancellationToken);
            if (created.IsFailure)
            {
                return made.Count == 0 ? Result.Failure<IReadOnlyList<SubTaskDto>>(created.Error) : Result.Success<IReadOnlyList<SubTaskDto>>(made);
            }

            made.Add(created.Value);
        }

        return Result.Success<IReadOnlyList<SubTaskDto>>(made);
    }

    // --- work in progress ---

    public async Task<Result<WipSettingDto>> GetWipAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<WipSettingDto>(Error.Unauthorized())
            : Result.Success(new WipSettingDto(await repository.GetWipLimitAsync(tenantId, cancellationToken)));

    public async Task<Result<WipSettingDto>> SetWipAsync(WipSettingDto request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<WipSettingDto>(Error.Unauthorized());
        }

        if (request.LimitPerUser is < 1 or > 1000)
        {
            return Result.Failure<WipSettingDto>(Error.Validation("The limit must be between 1 and 1000, or empty for no limit."));
        }

        await repository.SetWipLimitAsync(tenantId, request.LimitPerUser, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(request);
    }
}

/// <summary>The rules that stand between a task and a status (called by TaskService on every status change).</summary>
public static class TaskPlanningRules
{
    /// <summary>A refusal message for moving the task to <paramref name="newStatus"/>, or null when it may.</summary>
    public static async Task<string?> CheckAsync(
        ITaskPlanningRepository repository, TaskItem task, TaskItemStatus newStatus, Guid? actingUserId, CancellationToken cancellationToken)
    {
        if (newStatus == TaskItemStatus.Completed && task.Status != TaskItemStatus.Completed)
        {
            var open = await repository.CountOpenBlockersAsync(task.TenantId, task.Id, cancellationToken);
            if (open > 0)
            {
                return $"This task waits for {open} unfinished task(s).";
            }

            var unmet = await repository.CountUnmetCriteriaAsync(task.TenantId, task.Id, cancellationToken);
            if (unmet > 0)
            {
                return $"{unmet} acceptance criteria are not met yet.";
            }
        }

        if (newStatus == TaskItemStatus.InProgress && task.Status != TaskItemStatus.InProgress
            && await repository.GetWipLimitAsync(task.TenantId, cancellationToken) is { } limit
            && (task.AssignedUserId ?? actingUserId) is { } person
            && await repository.CountInProgressAsync(task.TenantId, person, task.Id, cancellationToken) >= limit)
        {
            return $"The limit of {limit} tasks in progress per person is reached; finish or pause one first.";
        }

        return null;
    }
}
