using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class SprintService(
    ISprintRepository sprintRepository,
    ISprintEventRepository eventRepository,
    IAgileTaskRepository taskRepository,
    IAgileUnitOfWork unitOfWork,
    SprintTracker tracker) : ISprintService
{
    public async Task<Result<IReadOnlyList<SprintDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var sprints = await sprintRepository.ListByProjectAsync(projectId, cancellationToken);
        var tasks = await taskRepository.ListByProjectAsync(projectId, null, cancellationToken);
        return Result.Success<IReadOnlyList<SprintDto>>(sprints.OrderBy(s => s.Number).Select(s => ToDto(s, tasks)).ToList());
    }

    public async Task<Result<SprintDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        return sprint is null
            ? Result.Failure<SprintDto>(Error.NotFound("Sprint not found."))
            : Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    public async Task<Result<SprintDto>> CreateAsync(CreateSprintRequest request, CancellationToken cancellationToken)
    {
        var dateError = ValidateDates(request.StartDate, request.EndDate);
        if (dateError is not null)
        {
            return Result.Failure<SprintDto>(dateError);
        }

        var existing = await sprintRepository.ListByProjectAsync(request.ProjectId, cancellationToken);
        var number = existing.Count == 0 ? 1 : existing.Max(s => s.Number) + 1;
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"Sprint {number}" : request.Name;

        var sprint = new Sprint(Guid.NewGuid(), request.TenantId, request.ProjectId, number, name, request.Goal, request.StartDate, request.EndDate);
        await sprintRepository.AddAsync(sprint, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    public async Task<Result<SprintDto>> UpdateAsync(Guid id, UpdateSprintRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<SprintDto>(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status == SprintStatus.Completed)
        {
            return Result.Failure<SprintDto>(Error.Conflict("A completed sprint can no longer be edited."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<SprintDto>(Error.Validation("Name is required."));
        }

        var dateError = ValidateDates(request.StartDate, request.EndDate);
        if (dateError is not null)
        {
            return Result.Failure<SprintDto>(dateError);
        }

        sprint.UpdateDetails(request.Name, request.Goal, request.StartDate, request.EndDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    public async Task<Result<SprintDto>> StartAsync(Guid id, StartSprintRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<SprintDto>(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status != SprintStatus.Planned)
        {
            return Result.Failure<SprintDto>(Error.Conflict("Only a planned sprint can be started."));
        }

        var start = request.StartDate ?? sprint.StartDate;
        var end = request.EndDate ?? sprint.EndDate;
        if (start is null || end is null)
        {
            return Result.Failure<SprintDto>(Error.Validation("A sprint needs a start date and an end date before it can be started."));
        }

        var dateError = ValidateDates(start, end);
        if (dateError is not null)
        {
            return Result.Failure<SprintDto>(dateError);
        }

        var siblings = await sprintRepository.ListByProjectAsync(sprint.ProjectId, cancellationToken);
        if (siblings.Any(s => s.Status == SprintStatus.Active))
        {
            return Result.Failure<SprintDto>(Error.Conflict("The project already has an active sprint; complete it first."));
        }

        sprint.UpdateDetails(sprint.Name, sprint.Goal, start, end);
        sprint.Start();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    public async Task<Result<CompleteSprintResultDto>> CompleteAsync(Guid id, CompleteSprintRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<CompleteSprintResultDto>(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status != SprintStatus.Active)
        {
            return Result.Failure<CompleteSprintResultDto>(Error.Conflict("Only an active sprint can be completed."));
        }

        Sprint? target = null;
        if (request.MoveIncompleteToSprintId is { } targetId)
        {
            target = await sprintRepository.GetByIdAsync(targetId, cancellationToken);
            if (target is null || target.ProjectId != sprint.ProjectId)
            {
                return Result.Failure<CompleteSprintResultDto>(Error.Validation("The target sprint must be a sprint of the same project."));
            }

            if (target.Id == sprint.Id || target.Status == SprintStatus.Completed)
            {
                return Result.Failure<CompleteSprintResultDto>(Error.Conflict("Unfinished tasks cannot move to a completed sprint."));
            }
        }

        // Close the sprint first: its history stops changing, so moving the unfinished tasks out
        // below does not shrink the scope the finished sprint reports. The tasks that leave are
        // noted as carried over instead.
        sprint.Complete();

        var inSprint = (await taskRepository.ListByProjectAsync(sprint.ProjectId, sprint.Number, cancellationToken)).ToList();
        var unfinished = inSprint.Where(t => t.Status != AgileTaskStatus.Done).ToList();
        foreach (var task in unfinished)
        {
            var before = TaskSprintState.Of(task);
            await tracker.RecordCarriedOverAsync(task.TenantId, task.ProjectId, sprint.Number, task.Id, before.Points, cancellationToken);
            task.AssignSprint(target?.Number);
            await tracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new CompleteSprintResultDto(await ToDtoAsync(sprint, cancellationToken), inSprint.Count - unfinished.Count, unfinished.Count));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status != SprintStatus.Planned)
        {
            return Result.Failure(Error.Conflict("Only a planned sprint can be deleted; an active or completed one is part of the project's history."));
        }

        if ((await taskRepository.ListByProjectAsync(sprint.ProjectId, sprint.Number, cancellationToken)).Count > 0)
        {
            return Result.Failure(Error.Conflict("Move the sprint's tasks to the backlog or another sprint first."));
        }

        await eventRepository.RemoveRangeAsync(await eventRepository.ListBySprintAsync(sprint.ProjectId, sprint.Number, cancellationToken), cancellationToken);
        await sprintRepository.RemoveAsync(sprint, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<SprintDto>> AssignTasksAsync(Guid id, AssignSprintTasksRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<SprintDto>(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status == SprintStatus.Completed)
        {
            return Result.Failure<SprintDto>(Error.Conflict("Tasks cannot be added to a completed sprint."));
        }

        var ids = request.TaskIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Failure<SprintDto>(Error.Validation("Give at least one task."));
        }

        var tasks = await taskRepository.ListByIdsAsync(ids, cancellationToken);
        if (tasks.Count != ids.Count || tasks.Any(t => t.ProjectId != sprint.ProjectId))
        {
            return Result.Failure<SprintDto>(Error.NotFound("Every task must exist in the sprint's project."));
        }

        foreach (var task in tasks.Where(t => t.SprintNumber != sprint.Number))
        {
            var before = TaskSprintState.Of(task);
            task.AssignSprint(sprint.Number);
            await tracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    public async Task<Result<SprintDto>> RemoveTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (sprint is null)
        {
            return Result.Failure<SprintDto>(Error.NotFound("Sprint not found."));
        }

        if (sprint.Status == SprintStatus.Completed)
        {
            return Result.Failure<SprintDto>(Error.Conflict("A completed sprint's tasks stay where they were."));
        }

        var task = await taskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task is null || task.ProjectId != sprint.ProjectId || task.SprintNumber != sprint.Number)
        {
            return Result.Failure<SprintDto>(Error.NotFound("That task is not in this sprint."));
        }

        var before = TaskSprintState.Of(task);
        task.AssignSprint(null);
        await tracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(sprint, cancellationToken));
    }

    private static Error? ValidateDates(DateOnly? start, DateOnly? end) =>
        start is not null && end is not null && end < start
            ? Error.Validation("The end date cannot be before the start date.")
            : null;

    private async Task<SprintDto> ToDtoAsync(Sprint sprint, CancellationToken cancellationToken) =>
        ToDto(sprint, await taskRepository.ListByProjectAsync(sprint.ProjectId, sprint.Number, cancellationToken));

    private static SprintDto ToDto(Sprint sprint, IReadOnlyList<AgileTask> projectTasks)
    {
        var inSprint = projectTasks.Where(t => t.SprintNumber == sprint.Number).ToList();
        return new SprintDto(
            sprint.Id, sprint.TenantId, sprint.ProjectId, sprint.Number, sprint.Name, sprint.Goal,
            sprint.StartDate, sprint.EndDate, sprint.Status,
            inSprint.Count, inSprint.Sum(t => t.StoryPoints ?? 0),
            inSprint.Where(t => t.Status == AgileTaskStatus.Done).Sum(t => t.StoryPoints ?? 0));
    }
}
