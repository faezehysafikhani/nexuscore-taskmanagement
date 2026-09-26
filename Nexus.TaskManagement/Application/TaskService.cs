using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

public sealed class TaskService(
    ITaskRepository repository,
    ITagRepository tagRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ITaskActivityService activity,
    IFileStorage fileStorage,
    ITaskAccessScope access) : ITaskService
{
    /// <summary>
    /// Seeing a task (owner, assignee, collaborator, team) lets a user work on it; changing what
    /// the task is - details, priority, assignment, schedule - or deleting it is for its owner
    /// and for Tasks.ManageAll holders. Same rule as the task screen.
    /// </summary>
    internal static Error NotTaskOwner() =>
        Error.Forbidden("Only the owner of this task, or a user who manages all tasks, can do this.");

    private bool CanChangeStatus(TaskItem task) =>
        access.CanManage(task)
        || (task.AllowAssigneeStatusUpdate
            && currentUser.UserId is { } actor
            && task.IsResponsible(actor));

    public async Task<Result<PagedResult<TaskListItemDto>>> ListAsync(
        ListTasksRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<PagedResult<TaskListItemDto>>(Error.Unauthorized());
        }

        var normalized = request with
        {
            TenantId = currentUser.TenantId.Value,
            PageNumber = Math.Max(1, request.PageNumber),
            PageSize = Math.Clamp(request.PageSize, 1, 200)
        };

        var page = await repository.ListAsync(normalized, cancellationToken);

        var userIds = page.Items
            .Where(t => t.AssignedUserId.HasValue)
            .Select(t => t.AssignedUserId!.Value)
            .Distinct()
            .ToList();
        var groupIds = page.Items
            .Where(t => t.AssignedUserGroupId.HasValue)
            .Select(t => t.AssignedUserGroupId!.Value)
            .Distinct()
            .ToList();

        var users = await repository.GetUserSummariesAsync(userIds, cancellationToken);
        var groups = await repository.GetUserGroupSummariesAsync(groupIds, cancellationToken);

        var items = page.Items.Select(task => new TaskListItemDto(
            task.Id,
            task.Title,
            task.IsProject,
            task.Recurrence is not null,
            task.Status,
            task.Priority,
            task.DueDate,
            Lookup(users, task.AssignedUserId),
            Lookup(groups, task.AssignedUserGroupId),
            task.SubTasks.Count,
            task.SubTasks.Count(s => s.IsCompleted),
            task.Tags.Where(t => t.Tag is not null).Select(t => ToDto(t.Tag!)).ToList(),
            task.CreatedAtUtc,
            task.ModifiedAtUtc,
            task.DueTime)).ToList();

        return Result.Success(new PagedResult<TaskListItemDto>(
            items, page.PageNumber, page.PageSize, page.TotalCount));
    }

    public async Task<Result<TaskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var task = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        return task is null
            ? Result.Failure<TaskDto>(Error.NotFound("Task not found."))
            : Result.Success(await ToDtoAsync(task, cancellationToken));
    }

    public async Task<Result<TaskDto>> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;

        if (request.IsProject && (request.SubTasks is null || request.SubTasks.Count == 0))
        {
            return Result.Failure<TaskDto>(Error.Validation("A project must have at least one subtask."));
        }

        var referenceCheck = await ValidateReferencesAsync(
            tenantId, null, request.AssignedUserGroupId, request.AssigneeUserIds, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(referenceCheck.Error);
        }

        var responsible = FinalResponsible(null, request.ResponsibleUserIds, request.AssignedUserId);
        if (responsible.Count == 0)
        {
            return Result.Failure<TaskDto>(Error.Validation("Choose who is responsible for the task."));
        }

        var peopleCheck = await ValidateNewPeopleAsync(tenantId, null, responsible, request.AssigneeUserIds, cancellationToken);
        if (peopleCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(peopleCheck.Error);
        }

        var task = new TaskItem(
            Guid.NewGuid(), tenantId, request.Title, request.DueDate, request.Priority,
            request.IsProject, currentUser.UserId, request.Description);

        task.SetResponsibleUsers(responsible);
        task.UpdateDetails(
            request.Title, request.Description, request.DueDate, request.Priority,
            task.AssignedUserId, request.AssignedUserGroupId, request.AllowAssigneeStatusUpdate);
        task.SetDueTime(request.DueTime);

        task.UpdateCharter(
            request.CharterDescription, request.CharterProjectManager,
            request.CharterStartDate, request.CharterEndDate);
        task.SetCharterTimes(request.CharterStartTime, request.CharterEndTime);

        if (request.AssigneeUserIds is { Count: > 0 })
        {
            task.AssignUsers(request.AssigneeUserIds);
        }

        foreach (var (input, index) in (request.SubTasks ?? []).Select((s, i) => (s, i)))
        {
            var subTask = task.AddSubTask(
                Guid.NewGuid(), input.Title, input.Importance,
                input.SortOrder == 0 ? index : input.SortOrder);
            subTask.UpdateDetails(input.Title, input.Importance, input.StartDate, input.EndDate, subTask.SortOrder);
            subTask.SetTimes(input.StartTime, input.EndTime);
            subTask.MarkGeneratedOccurrence(input.IsGeneratedOccurrence);
        }

        if (request.Recurrence is { } recurrence)
        {
            var schedule = new RepetitiveTask(
                Guid.NewGuid(), tenantId, task.Id, recurrence.Frequency, recurrence.StartDate);
            ApplyRecurrence(schedule, recurrence);
            task.AttachRecurrence(schedule);
        }

        await repository.AddAsync(task, cancellationToken);

        if (request.Tags is { Count: > 0 })
        {
            await ApplyTagsAsync(tenantId, task.Id, request.Tags, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(task.Id, "Task created", task.Title, cancellationToken);

        var created = await repository.GetByIdAsync(tenantId, task.Id, cancellationToken);
        return Result.Success(await ToDtoAsync(created!, cancellationToken));
    }

    public async Task<Result<TaskDto>> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<TaskDto>(NotTaskOwner());
        }

        var referenceCheck = await ValidateReferencesAsync(
            tenantId, null, request.AssignedUserGroupId, request.AssigneeUserIds, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(referenceCheck.Error);
        }

        var responsible = FinalResponsible(task, request.ResponsibleUserIds, request.AssignedUserId);
        var peopleCheck = await ValidateNewPeopleAsync(tenantId, task, responsible, request.AssigneeUserIds, cancellationToken);
        if (peopleCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(peopleCheck.Error);
        }

        if (request.IsProject is true && !task.IsProject)
        {
            var subTaskCount = await repository.CountSubTasksAsync(tenantId, id, cancellationToken);
            if (subTaskCount == 0)
            {
                return Result.Failure<TaskDto>(
                    Error.Validation("Add at least one subtask before turning this task into a project."));
            }
        }

        var (previousResponsible, previousDueDate, previousDueTime) = (task.ResponsibleUserIds, task.DueDate, task.DueTime);
        task.SetResponsibleUsers(responsible);
        task.UpdateDetails(
            request.Title, request.Description, request.DueDate, request.Priority,
            task.AssignedUserId, request.AssignedUserGroupId, request.AllowAssigneeStatusUpdate);
        task.SetDueTime(request.DueTime);
        task.RecordAssignmentAndDueChanges(previousResponsible, previousDueDate, previousDueTime, currentUser.UserId);

        task.UpdateCharter(
            request.CharterDescription, request.CharterProjectManager,
            request.CharterStartDate, request.CharterEndDate);
        task.SetCharterTimes(request.CharterStartTime, request.CharterEndTime);

        if (request.IsProject is { } isProject)
        {
            task.SetIsProject(isProject);
        }

        if (request.AssigneeUserIds is not null)
        {
            task.AssignUsers(request.AssigneeUserIds);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(id, "Task updated", task.Title, cancellationToken);

        var updated = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        return Result.Success(await ToDtoAsync(updated!, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var task = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure(NotTaskOwner());
        }

        var storedFiles = await repository.ClearLinksForTaskAsync(id, cancellationToken);
        repository.Remove(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteStoredFilesAsync(storedFiles, cancellationToken);
        await activity.RecordAsync(id, "Task deleted", task.Title, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<TaskDto>> ChangeStatusAsync(
        Guid id, ChangeTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        if (!CanChangeStatus(task))
        {
            return Result.Failure<TaskDto>(
                Error.Forbidden("Only the task owner, a task manager, or the assigned user when status updates are allowed can change the status."));
        }

        var previous = task.Status;
        task.ChangeStatus(request.Status, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(id, "Status changed", $"{previous} -> {request.Status}", cancellationToken);

        var updated = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        return Result.Success(await ToDtoAsync(updated!, cancellationToken));
    }

    public async Task<Result<TaskDto>> ChangePriorityAsync(
        Guid id, ChangeTaskPriorityRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<TaskDto>(NotTaskOwner());
        }

        var previous = task.Priority;
        task.UpdateDetails(
            task.Title, task.Description, task.DueDate, request.Priority,
            task.AssignedUserId, task.AssignedUserGroupId, task.AllowAssigneeStatusUpdate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(id, "Priority changed", $"{previous} -> {request.Priority}", cancellationToken);

        var updated = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        return Result.Success(await ToDtoAsync(updated!, cancellationToken));
    }

    public async Task<Result<TaskDto>> AssignUserAsync(
        Guid id, AssignUserRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<TaskDto>(NotTaskOwner());
        }

        var referenceCheck = await ValidateReferencesAsync(
            tenantId, null, null, request.AssigneeUserIds, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(referenceCheck.Error);
        }

        var responsible = FinalResponsible(task, request.ResponsibleUserIds, request.AssignedUserId);
        var peopleCheck = await ValidateNewPeopleAsync(tenantId, task, responsible, request.AssigneeUserIds, cancellationToken);
        if (peopleCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(peopleCheck.Error);
        }

        var previousResponsible = task.ResponsibleUserIds;
        task.SetResponsibleUsers(responsible);
        task.RecordAssignmentAndDueChanges(previousResponsible, task.DueDate, task.DueTime, currentUser.UserId);

        if (request.AssigneeUserIds is not null)
        {
            task.AssignUsers(request.AssigneeUserIds);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(id, "Assignee changed", string.Join(",", task.ResponsibleUserIds), cancellationToken);

        var updated = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        return Result.Success(await ToDtoAsync(updated!, cancellationToken));
    }

    public async Task<Result<TaskDto>> AssignUserGroupAsync(
        Guid id, AssignUserGroupRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<TaskDto>(NotTaskOwner());
        }

        var referenceCheck = await ValidateReferencesAsync(
            tenantId, null, request.AssignedUserGroupId, null, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<TaskDto>(referenceCheck.Error);
        }

        task.UpdateDetails(
            task.Title, task.Description, task.DueDate, task.Priority,
            task.AssignedUserId, request.AssignedUserGroupId, task.AllowAssigneeStatusUpdate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(id, "Team changed", request.AssignedUserGroupId?.ToString(), cancellationToken);

        var updated = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        return Result.Success(await ToDtoAsync(updated!, cancellationToken));
    }

    // -----------------------------------------------------------------------
    // Subtasks
    // -----------------------------------------------------------------------

    public async Task<Result<SubTaskDto>> CreateSubTaskAsync(
        Guid taskId, CreateSubTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<SubTaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var task = await repository.GetForUpdateAsync(tenantId, taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<SubTaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<SubTaskDto>(NotTaskOwner());
        }

        var count = await repository.CountSubTasksAsync(tenantId, taskId, cancellationToken);
        var subTask = task.AddSubTask(
            Guid.NewGuid(), request.Title, request.Importance,
            request.SortOrder == 0 ? count : request.SortOrder);
        subTask.UpdateDetails(request.Title, request.Importance, request.StartDate, request.EndDate, subTask.SortOrder);
        subTask.SetTimes(request.StartTime, request.EndTime);
        subTask.MarkGeneratedOccurrence(request.IsGeneratedOccurrence);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(taskId, "Subtask added", request.Title, cancellationToken);

        return Result.Success(ToDto(subTask));
    }

    public async Task<Result<SubTaskDto>> UpdateSubTaskAsync(
        Guid subTaskId, UpdateSubTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<SubTaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var subTask = await repository.GetSubTaskAsync(tenantId, subTaskId, cancellationToken);
        if (subTask is null)
        {
            return Result.Failure<SubTaskDto>(Error.NotFound("Subtask not found."));
        }

        var parent = await repository.GetForUpdateAsync(tenantId, subTask.TaskId, cancellationToken);
        if (parent is null)
        {
            return Result.Failure<SubTaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(parent))
        {
            return Result.Failure<SubTaskDto>(NotTaskOwner());
        }

        subTask.UpdateDetails(request.Title, request.Importance, request.StartDate, request.EndDate, request.SortOrder);
        subTask.SetTimes(request.StartTime, request.EndTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(subTask.TaskId, "Subtask updated", request.Title, cancellationToken);

        return Result.Success(ToDto(subTask));
    }

    public async Task<Result<SubTaskDto>> ChangeSubTaskStatusAsync(
        Guid subTaskId, ChangeSubTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<SubTaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var subTask = await repository.GetSubTaskAsync(tenantId, subTaskId, cancellationToken);
        if (subTask is null)
        {
            return Result.Failure<SubTaskDto>(Error.NotFound("Subtask not found."));
        }

        var parent = await repository.GetForUpdateAsync(tenantId, subTask.TaskId, cancellationToken);
        if (parent is null)
        {
            return Result.Failure<SubTaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(parent))
        {
            return Result.Failure<SubTaskDto>(NotTaskOwner());
        }

        subTask.SetCompleted(request.IsCompleted);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(
            subTask.TaskId, request.IsCompleted ? "Subtask completed" : "Subtask reopened",
            subTask.Title, cancellationToken);

        return Result.Success(ToDto(subTask));
    }

    public async Task<Result<SubTaskDto>> GetSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<SubTaskDto>(Error.Unauthorized());
        }

        var subTask = await repository.GetSubTaskAsync(currentUser.TenantId.Value, subTaskId, cancellationToken);
        return subTask is null
            ? Result.Failure<SubTaskDto>(Error.NotFound("Subtask not found."))
            : Result.Success(ToDto(subTask));
    }

    public async Task<Result<IReadOnlyList<SubTaskDto>>> GetSubTasksAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<SubTaskDto>>(Error.Unauthorized());
        }

        var subTasks = await repository.GetSubTasksAsync(currentUser.TenantId.Value, taskId, cancellationToken);
        return Result.Success<IReadOnlyList<SubTaskDto>>(subTasks.Select(ToDto).ToList());
    }

    public async Task<Result> DeleteSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var subTask = await repository.GetSubTaskAsync(tenantId, subTaskId, cancellationToken);
        if (subTask is null)
        {
            return Result.Failure(Error.NotFound("Subtask not found."));
        }

        var parent = await repository.GetForUpdateAsync(tenantId, subTask.TaskId, cancellationToken);
        if (parent is null)
        {
            return Result.Failure(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(parent))
        {
            return Result.Failure(NotTaskOwner());
        }

        if (parent.IsProject)
        {
            var count = await repository.CountSubTasksAsync(tenantId, subTask.TaskId, cancellationToken);
            if (count <= 1)
            {
                return Result.Failure(
                    Error.Validation("A project must keep at least one subtask. Delete the project instead."));
            }
        }

        var storedFiles = await repository.ClearLinksForSubTaskAsync(subTaskId, cancellationToken);
        repository.RemoveSubTask(subTask);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteStoredFilesAsync(storedFiles, cancellationToken);
        await activity.RecordAsync(subTask.TaskId, "Subtask deleted", subTask.Title, cancellationToken);

        return Result.Success();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Result> ValidateReferencesAsync(
        Guid tenantId,
        Guid? assignedUserId,
        Guid? assignedUserGroupId,
        IReadOnlyList<Guid>? assigneeUserIds,
        CancellationToken cancellationToken)
    {
        if (assignedUserId is { } userId && !await repository.UserExistsAsync(tenantId, userId, cancellationToken))
        {
            return Result.Failure(Error.Validation("The assigned user does not exist in this tenant."));
        }

        if (assignedUserGroupId is { } groupId
            && !await repository.UserGroupExistsAsync(tenantId, groupId, cancellationToken))
        {
            return Result.Failure(Error.Validation("The assigned team does not exist in this tenant."));
        }

        foreach (var id in (assigneeUserIds ?? []).Distinct())
        {
            if (!await repository.UserExistsAsync(tenantId, id, cancellationToken))
            {
                return Result.Failure(Error.Validation($"User {id} does not exist in this tenant."));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Who is responsible after a request: its ResponsibleUserIds when sent; otherwise the single
    /// AssignedUserId older clients send - naming one of the current responsible people keeps
    /// them all (that one first), naming someone else makes them the only one, none clears it.
    /// </summary>
    private static List<Guid> FinalResponsible(TaskItem? task, IReadOnlyList<Guid>? requested, Guid? single)
    {
        if (requested is not null)
        {
            return requested.Where(id => id != Guid.Empty).Distinct().ToList();
        }

        var current = task?.ResponsibleUserIds ?? [];
        if (single is { } named && named != Guid.Empty)
        {
            return current.Contains(named) ? current.Where(id => id != named).Prepend(named).ToList() : [named];
        }

        return [];
    }

    /// <summary>
    /// Someone newly made responsible for the task, or newly given access to it, must be an
    /// active user of the task's organization. People already on the task may stay as they are.
    /// </summary>
    private async Task<Result> ValidateNewPeopleAsync(
        Guid tenantId, TaskItem? task, IReadOnlyList<Guid> responsible, IReadOnlyList<Guid>? accessList, CancellationToken cancellationToken)
    {
        foreach (var id in responsible.Where(id => task is null || !task.IsResponsible(id)))
        {
            if (!await repository.ActiveUserExistsAsync(tenantId, id, cancellationToken))
            {
                return Result.Failure(Error.Validation("The responsible user is not an active user of this organization."));
            }
        }

        foreach (var id in (accessList ?? []).Distinct().Where(id => task is null || task.Assignees.All(a => a.UserId != id)))
        {
            if (!responsible.Contains(id) && !await repository.ActiveUserExistsAsync(tenantId, id, cancellationToken))
            {
                return Result.Failure(Error.Validation("Only active users of this organization can be given access to the task."));
            }
        }

        return Result.Success();
    }

    private async Task ApplyTagsAsync(
        Guid tenantId, Guid taskId, IReadOnlyList<string> tagNames, CancellationToken cancellationToken)
    {
        foreach (var name in tagNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var tag = await tagRepository.GetByNameAsync(tenantId, name, cancellationToken);
            if (tag is null)
            {
                tag = new Tag(Guid.NewGuid(), tenantId, name);
                await tagRepository.AddAsync(tag, cancellationToken);
            }

            var existing = await tagRepository.FindLinkAsync(tag.Id, taskId, null, cancellationToken);
            if (existing is null)
            {
                await tagRepository.AddLinkAsync(TaskTag.ForTask(Guid.NewGuid(), tag.Id, taskId), cancellationToken);
            }
        }
    }

    internal static void ApplyRecurrence(RepetitiveTask schedule, RecurrenceInput input) =>
        schedule.UpdateSchedule(
            input.Frequency, input.IntervalWeeks, input.StartTime, input.EndTime,
            input.WeeklyDays, input.MonthlyDays, input.NthOccurrence, input.NthWeekday,
            input.StartDate, input.EndDate);

    private static TValue? Lookup<TValue>(IReadOnlyDictionary<Guid, TValue> source, Guid? key)
        where TValue : class =>
        key is { } id && source.TryGetValue(id, out var value) ? value : null;

    public async Task<Result> AddActivityEntryAsync(Guid taskId, CreateTaskActivityRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.Action) || request.Action.Trim().Length > 120)
        {
            return Result.Failure(Error.Validation("An activity action of 1-120 characters is required."));
        }

        if (request.Details is { Length: > 2000 })
        {
            return Result.Failure(Error.Validation("Activity details can be at most 2000 characters."));
        }

        if (await repository.GetForUpdateAsync(currentUser.TenantId.Value, taskId, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound("Task not found."));
        }

        await activity.RecordAsync(taskId, request.Action.Trim(), request.Details?.Trim(), cancellationToken);
        return Result.Success();
    }

    /// <summary>Removes file contents after the rows are gone (never before: a failed save keeps both).</summary>
    private async Task DeleteStoredFilesAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        foreach (var key in storageKeys)
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }
    }

    internal static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Color);

    internal static TaskFileDto ToDto(TaskFile link) => new(
        link.Id,
        link.FileId,
        link.File?.OriginalFileName ?? string.Empty,
        link.File?.ContentType ?? string.Empty,
        link.File?.FileSizeBytes ?? 0,
        link.File?.UploadedByUserId,
        link.File?.CreatedAtUtc ?? default);

    internal static SubTaskDto ToDto(SubTask subTask) => new(
        subTask.Id,
        subTask.TaskId,
        subTask.Title,
        subTask.StartDate,
        subTask.EndDate,
        subTask.Importance,
        subTask.IsCompleted,
        subTask.SortOrder,
        subTask.Tags.Where(t => t.Tag is not null).Select(t => ToDto(t.Tag!)).ToList(),
        subTask.Files.Select(ToDto).ToList(),
        subTask.CreatedAtUtc,
        subTask.IsGeneratedOccurrence,
        subTask.StartTime,
        subTask.EndTime);

    internal static RepetitiveTaskDto ToDto(RepetitiveTask schedule) => new(
        schedule.Id,
        schedule.TaskId,
        schedule.Frequency,
        schedule.IntervalWeeks,
        schedule.StartTime,
        schedule.EndTime,
        schedule.WeeklyDays,
        schedule.MonthlyDays,
        schedule.NthOccurrence,
        schedule.NthWeekday,
        schedule.StartDate,
        schedule.EndDate,
        schedule.NextExecutionAtUtc,
        schedule.LastExecutionAtUtc,
        schedule.IsActive);

    private async Task<TaskDto> ToDtoAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var userIds = new List<Guid>();
        if (task.OwnerUserId is { } owner) userIds.Add(owner);
        userIds.AddRange(task.ResponsibleUserIds);
        userIds.AddRange(task.Assignees.Select(a => a.UserId));

        var users = await repository.GetUserSummariesAsync(userIds.Distinct().ToList(), cancellationToken);
        var groups = task.AssignedUserGroupId is { } groupId
            ? await repository.GetUserGroupSummariesAsync([groupId], cancellationToken)
            : new Dictionary<Guid, UserGroupSummaryDto>();

        return new TaskDto(
            task.Id,
            task.Title,
            task.Description,
            task.IsProject,
            task.Recurrence is not null,
            task.Status,
            task.Priority,
            task.DueDate,
            task.ActualCompletionDateUtc,
            Lookup(users, task.OwnerUserId),
            Lookup(users, task.AssignedUserId),
            Lookup(groups, task.AssignedUserGroupId),
            task.Assignees
                .Select(a => users.TryGetValue(a.UserId, out var u) ? u : null)
                .Where(u => u is not null)
                .Select(u => u!)
                .ToList(),
            task.AllowAssigneeStatusUpdate,
            task.CharterDescription,
            task.CharterProjectManager,
            task.CharterStartDate,
            task.CharterEndDate,
            task.SubTasks.OrderBy(s => s.SortOrder).Select(ToDto).ToList(),
            task.Tags.Where(t => t.Tag is not null).Select(t => ToDto(t.Tag!)).ToList(),
            task.Files.Select(ToDto).ToList(),
            task.Recurrence is null ? null : ToDto(task.Recurrence),
            task.CreatedAtUtc,
            task.ModifiedAtUtc,
            task.DueTime,
            task.CharterStartTime,
            task.CharterEndTime,
            task.ResponsibleUserIds
                .Select(id => users.TryGetValue(id, out var u) ? u : null)
                .Where(u => u is not null)
                .Select(u => u!)
                .ToList());
    }
}
