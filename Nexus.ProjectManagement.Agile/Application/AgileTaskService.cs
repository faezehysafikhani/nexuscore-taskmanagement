using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public sealed class AgileTaskService(
    IAgileTaskRepository repository,
    IAgileUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IAgileTaskService
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

        var task = new AgileTask(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Title);
        task.UpdateDetails(request.Title, request.Description, request.ResponsibleUserId, request.ApproverUserId,
            request.DueDate, request.Priority, request.SprintNumber);

        await repository.AddAsync(task, cancellationToken);
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

        task.UpdateDetails(request.Title, request.Description, request.ResponsibleUserId, request.ApproverUserId,
            request.DueDate, request.Priority, request.SprintNumber);
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

        task.ChangeStatus(request.Status);
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

        await repository.RemoveAsync(task, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
        task.ResponsibleUserId, task.ApproverUserId, task.DueDate, task.Priority, task.SprintNumber, task.ApprovalStatus);
}
