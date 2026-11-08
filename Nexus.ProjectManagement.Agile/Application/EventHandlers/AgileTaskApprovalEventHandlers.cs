using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Application.EventHandlers;

public sealed class AgileTaskApprovalGrantedHandler(IAgileTaskRepository repository, IAgileUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "AgileTask")
        {
            return;
        }

        var task = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (task is null)
        {
            return;
        }

        task.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AgileTaskApprovalRejectedHandler(IAgileTaskRepository repository, IAgileUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "AgileTask")
        {
            return;
        }

        var task = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (task is null)
        {
            return;
        }

        task.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A status change was confirmed: the task moves to the column that was asked for.</summary>
public sealed class AgileTaskStatusApprovalGrantedHandler(
    IAgileTaskStatusSubmissionRepository submissions, IAgileTaskService tasks, IAgileUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != AgileTaskStatusSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await tasks.ChangeStatusAsync(submission.TaskId, new Dtos.ChangeAgileTaskStatusRequest(submission.SubmittedStatus), cancellationToken);
    }
}

public sealed class AgileTaskStatusApprovalRejectedHandler(IAgileTaskStatusSubmissionRepository submissions, IAgileUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != AgileTaskStatusSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
