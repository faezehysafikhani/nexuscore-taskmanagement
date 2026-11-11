using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// "Submit for review": the person doing a task asks for it to be checked. The approval backend (Workflow, when the host
/// installs it) decides; this module only knows <see cref="IApprovalRequester"/> and the two events it raises, so with no
/// backend - or no approval chain set up for tasks - the task is simply completed, like every other optional approval.
/// </summary>
public sealed class TaskReviewService(
    ITaskRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ITaskAccessScope access,
    IApprovalRequester approvalRequester,
    ITaskActivityService activity,
    ITaskService tasks,
    NexusCore.Application.Identity.Interfaces.IManagementChainProvider? managers = null) : ITaskReviewService
{
    public const string SubjectType = "TaskReview";

    public async Task<Result<TaskDto>> SubmitAsync(Guid id, SubmitTaskReviewRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var task = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Task not found."));
        }

        var actor = currentUser.UserId;
        if (!(access.CanManage(task) || (task.AllowAssigneeStatusUpdate && actor is { } who && task.IsResponsible(who))))
        {
            return Result.Failure<TaskDto>(Error.Forbidden("Only the task owner, a task manager, or the assigned user can submit it for review."));
        }

        if (task.Status == TaskItemStatus.Completed)
        {
            return Result.Failure<TaskDto>(Error.Validation("The task is already completed."));
        }

        if (task.IsInReview)
        {
            return Result.Failure<TaskDto>(Error.Conflict("This task is already waiting for review."));
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var now = DateTimeOffset.UtcNow;
        var subject = new ApprovalSubject(SubjectType, task.Id, tenantId, Participants: await ParticipantsAsync(task, actor, cancellationToken));
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            task.SubmitForReview(actor, comment, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await activity.RecordAsync(id, "Submitted for review", comment, cancellationToken);
        }
        else
        {
            // No approval chain: nobody to wait for.
            task.SubmitForReview(actor, comment, now);
            task.ApproveReview(actor, comment, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await activity.RecordAsync(id, "Completed (no review configured)", comment, cancellationToken);
        }

        return await tasks.GetAsync(id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<TaskReviewItemDto>>> ListInReviewAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<TaskReviewItemDto>>(Error.Unauthorized());
        }

        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<TaskReviewItemDto>>([]);
        }

        var waiting = await repository.GetInReviewAsync(tenantId, ids.Distinct().Take(200).ToList(), cancellationToken);
        var people = await repository.GetUserSummariesAsync(
            waiting.Where(t => t.ReviewSubmittedByUserId.HasValue).Select(t => t.ReviewSubmittedByUserId!.Value).Distinct().ToList(),
            cancellationToken);
        return Result.Success<IReadOnlyList<TaskReviewItemDto>>(waiting.Select(t => new TaskReviewItemDto(
            t.Id, t.Title, t.DueDate,
            t.ReviewSubmittedByUserId is { } by && people.TryGetValue(by, out var user) ? user : null,
            t.ReviewSubmittedAtUtc, t.ReviewComment)).ToList());
    }

    /// <summary>
    /// The people a review step can name: Owner is whoever owns the task. Manager (and Approver1/2) are the submitter's
    /// managers in the organization structure when it is installed, and the owner otherwise.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Guid>> ParticipantsAsync(TaskItem task, Guid? submitter, CancellationToken cancellationToken)
    {
        var participants = new Dictionary<string, Guid>();
        if (task.OwnerUserId is { } owner)
        {
            participants[ApprovalParticipants.Owner] = owner;
            participants[ApprovalParticipants.Manager] = owner;
            participants[ApprovalParticipants.Approver1] = owner;
        }

        if (managers is not null && submitter is { } who)
        {
            var chain = await managers.GetChainAsync(task.TenantId, who, cancellationToken);
            if (chain.Count > 0)
            {
                participants[ApprovalParticipants.Manager] = chain[0];
                participants[ApprovalParticipants.Approver1] = chain[0];
            }

            if (chain.Count > 1)
            {
                participants[ApprovalParticipants.Approver2] = chain[1];
            }
        }

        return participants;
    }
}

/// <summary>The review was approved: the task is done.</summary>
public sealed class TaskReviewApprovedHandler(ITaskRepository repository, ITaskManagementUnitOfWork unitOfWork, ITaskActivityService activity)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != TaskReviewService.SubjectType)
        {
            return;
        }

        var task = await repository.GetPeopleOfAsync(domainEvent.TenantId, domainEvent.SubjectId, cancellationToken);
        if (task is null || !task.IsInReview)
        {
            return;
        }

        task.ApproveReview(domainEvent.DecidedByUserId, domainEvent.Comment, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(task.Id, "Review approved", domainEvent.Comment, cancellationToken);
    }
}

/// <summary>The review was rejected: the task goes back to work and can be submitted again.</summary>
public sealed class TaskReviewRejectedHandler(ITaskRepository repository, ITaskManagementUnitOfWork unitOfWork, ITaskActivityService activity)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != TaskReviewService.SubjectType)
        {
            return;
        }

        var task = await repository.GetPeopleOfAsync(domainEvent.TenantId, domainEvent.SubjectId, cancellationToken);
        if (task is null || !task.IsInReview)
        {
            return;
        }

        task.RejectReview(domainEvent.DecidedByUserId, domainEvent.Comment, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await activity.RecordAsync(task.Id, "Review rejected - back to work", domainEvent.Comment, cancellationToken);
    }
}
