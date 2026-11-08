using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

/// <summary>
/// Asking for a task to move to another column and have someone confirm it. Works with or without an approval backend: with none the
/// task moves at once, like every other optional approval in the project-management modules.
/// </summary>
public sealed class AgileTaskStatusSubmissionService(
    IAgileTaskStatusSubmissionRepository repository,
    IAgileTaskRepository taskRepository,
    IAgileTaskService taskService,
    IAgileUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IAgileTaskStatusSubmissionService
{
    public const string SubjectType = "AgileTaskStatus";

    public async Task<Result<IReadOnlyList<AgileTaskStatusSubmissionDto>>> ListForProjectsAsync(ListAgileForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<AgileTaskStatusSubmissionDto>>([]);
        }

        var items = await repository.ListByProjectsAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<AgileTaskStatusSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AgileTaskStatusSubmissionDto>>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var items = await repository.ListByTaskAsync(taskId, cancellationToken);
        return Result.Success<IReadOnlyList<AgileTaskStatusSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<AgileTaskStatusSubmissionDto>> SubmitAsync(SubmitAgileTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<AgileTaskStatusSubmissionDto>(Error.Validation("Unknown task status."));
        }

        var task = await taskRepository.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || task.ProjectId != request.ProjectId)
        {
            return Result.Failure<AgileTaskStatusSubmissionDto>(Error.NotFound("Agile task not found."));
        }

        if (task.Status == request.Status)
        {
            return Result.Failure<AgileTaskStatusSubmissionDto>(Error.Validation("The task is already in that column."));
        }

        var earlier = await repository.ListByTaskAsync(task.Id, cancellationToken);
        if (earlier.Any(s => s.ApprovalStatus == ApprovalStatus.PendingApproval))
        {
            return Result.Failure<AgileTaskStatusSubmissionDto>(Error.Conflict("An earlier status change for this task is still waiting for approval."));
        }

        var submission = new AgileTaskStatusSubmission(
            Guid.NewGuid(), request.TenantId, request.ProjectId, task.Id, task.Status, request.Status, request.Comment);
        await repository.AddAsync(submission, cancellationToken);

        var subject = new ApprovalSubject(SubjectType, submission.Id, submission.TenantId, ScopeType: "Project", ScopeId: submission.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);
        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            submission.MarkPendingApproval();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(submission));
        }

        submission.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var moved = await taskService.ChangeStatusAsync(task.Id, new ChangeAgileTaskStatusRequest(request.Status), cancellationToken);
        return moved.IsFailure ? Result.Failure<AgileTaskStatusSubmissionDto>(moved.Error) : Result.Success(ToDto(submission));
    }

    private static AgileTaskStatusSubmissionDto ToDto(AgileTaskStatusSubmission s) => new(
        s.Id, s.TenantId, s.ProjectId, s.TaskId, s.PreviousStatus, s.SubmittedStatus, s.Comment, s.ApprovalStatus,
        s.CreatedByUserId, s.CreatedAtUtc);
}
