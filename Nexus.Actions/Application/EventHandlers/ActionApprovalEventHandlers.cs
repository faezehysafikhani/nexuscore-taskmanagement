using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Application.EventHandlers;

public sealed class ActionApprovalGrantedHandler(IActionItemRepository repository, IActionsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Action")
        {
            return;
        }

        var action = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (action is null)
        {
            return;
        }

        action.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ActionApprovalRejectedHandler(IActionItemRepository repository, IActionsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Action")
        {
            return;
        }

        var action = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (action is null)
        {
            return;
        }

        action.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A status change was confirmed: the action takes the status that was asked for.</summary>
public sealed class ActionStatusApprovalGrantedHandler(
    IActionStatusSubmissionRepository submissions, IActionItemService actions, IActionsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != ActionStatusSubmissionService.SubjectType)
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
        await actions.ChangeStatusAsync(submission.ActionId, new Dtos.ChangeActionStatusRequest(submission.SubmittedStatus), cancellationToken);
    }
}

public sealed class ActionStatusApprovalRejectedHandler(IActionStatusSubmissionRepository submissions, IActionsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != ActionStatusSubmissionService.SubjectType)
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
