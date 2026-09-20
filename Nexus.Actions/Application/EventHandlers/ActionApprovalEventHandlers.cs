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
