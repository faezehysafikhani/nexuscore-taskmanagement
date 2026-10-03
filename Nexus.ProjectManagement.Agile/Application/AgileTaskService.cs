using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class AgileTaskService(
    IAgileTaskRepository repository,
    IAgileUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    ISprintRepository sprintRepository,
    IAgileChecklistRepository checklistRepository,
    SprintTracker sprintTracker) : IAgileTaskService
{
    public async Task<Result<IReadOnlyList<AgileTaskDto>>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken)
    {
        var tasks = await repository.ListByProjectAsync(projectId, sprintNumber, cancellationToken);
        return Result.Success<IReadOnlyList<AgileTaskDto>>(tasks.Select(ToDto).ToList());
    }

    public async Task<Result<AgileTaskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        return task is null
            ? Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."))
            : Result.Success(ToDto(task));
    }

    public async Task<Result<AgileTaskDto>> CreateAsync(CreateAgileTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<AgileTaskDto>(Error.Validation("Title is required."));
        }

        var sprintError = await ValidateSprintAsync(request.ProjectId, request.SprintNumber, cancellationToken);
        if (sprintError is not null)
        {
            return Result.Failure<AgileTaskDto>(sprintError);
        }

        var task = new AgileTask(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Title);
        task.UpdateDetails(request.Title, request.Description, request.ResponsibleUserId, request.ApproverUserId,
            request.DueDate, request.Priority, request.SprintNumber);
        task.SetStoryPoints(request.StoryPoints);
        task.SetRank(await NextRankAsync(request.ProjectId, task.Status, cancellationToken));

        await repository.AddAsync(task, cancellationToken);
        await sprintTracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, TaskSprintState.None, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    public async Task<Result<AgileTaskDto>> UpdateAsync(Guid id, UpdateAgileTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<AgileTaskDto>(Error.Validation("Title is required."));
        }

        // A sprint is checked only when the task is being moved into it, so editing a task that
        // already sits in a completed sprint keeps working.
        if (request.SprintNumber != task.SprintNumber)
        {
            var sprintError = await ValidateSprintAsync(task.ProjectId, request.SprintNumber, cancellationToken);
            if (sprintError is not null)
            {
                return Result.Failure<AgileTaskDto>(sprintError);
            }
        }

        var before = TaskSprintState.Of(task);
        task.UpdateDetails(request.Title, request.Description, request.ResponsibleUserId, request.ApproverUserId,
            request.DueDate, request.Priority, request.SprintNumber);
        // Omitted means "leave the estimate alone", so a client that predates story points cannot wipe it.
        if (request.StoryPoints is not null)
        {
            task.SetStoryPoints(request.StoryPoints);
        }

        await sprintTracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    public async Task<Result<AgileTaskDto>> SetStoryPointsAsync(Guid id, SetStoryPointsRequest request, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."));
        }

        var before = TaskSprintState.Of(task);
        task.SetStoryPoints(request.StoryPoints);
        await sprintTracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    public async Task<Result<AgileTaskDto>> ChangeStatusAsync(Guid id, ChangeAgileTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."));
        }

        var before = TaskSprintState.Of(task);
        if (request.Status != task.Status)
        {
            // A card that changes column lands at the bottom of the new one.
            task.SetRank(await NextRankAsync(task.ProjectId, request.Status, cancellationToken));
        }

        task.ChangeStatus(request.Status);
        await sprintTracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, before, TaskSprintState.Of(task), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return Result.Failure(Error.NotFound("Agile task not found."));
        }

        // The task's checklist goes with it, and a sprint that counted it no longer does.
        await checklistRepository.RemoveRangeAsync(await checklistRepository.ListByTaskAsync(id, cancellationToken), cancellationToken);
        await sprintTracker.RecordChangeAsync(task.TenantId, task.ProjectId, task.Id, TaskSprintState.Of(task), TaskSprintState.None, cancellationToken);
        await repository.RemoveAsync(task, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>A task cannot be put into a sprint that has already finished: its history is closed.</summary>
    private async Task<Error?> ValidateSprintAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken)
    {
        if (sprintNumber is not { } number)
        {
            return null;
        }

        var sprint = await sprintRepository.GetByNumberAsync(projectId, number, cancellationToken);
        return sprint is { Status: SprintStatus.Completed }
            ? Error.Conflict("Tasks cannot be added to a completed sprint.")
            : null;
    }

    private async Task<int> NextRankAsync(Guid projectId, AgileTaskStatus status, CancellationToken cancellationToken)
    {
        var column = (await repository.ListByProjectAsync(projectId, null, cancellationToken)).Where(t => t.Status == status).ToList();
        return column.Count == 0 ? 0 : column.Max(t => t.Rank) + 1;
    }

    public async Task<Result<AgileTaskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AgileTaskDto>(Error.NotFound("Agile task not found."));
        }

        var subject = new ApprovalSubject("AgileTask", task.Id, task.TenantId, ScopeType: "Project", ScopeId: task.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            task.MarkPendingApproval();
        }
        else
        {
            task.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    private static AgileTaskDto ToDto(AgileTask task) => new(
        task.Id, task.TenantId, task.ProjectId, task.Title, task.Description, task.Status,
        task.ResponsibleUserId, task.ApproverUserId, task.DueDate, task.Priority, task.SprintNumber, task.ApprovalStatus,
        task.StoryPoints, task.Rank);
}
