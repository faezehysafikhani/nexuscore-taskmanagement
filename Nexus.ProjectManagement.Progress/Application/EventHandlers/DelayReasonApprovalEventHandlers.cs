using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Progress.Application.EventHandlers;

public sealed class DelayReasonApprovalGrantedHandler(IDelayReasonRepository repository, IProgressUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != DelayReasonService.SubjectType)
        {
            return;
        }

        var reason = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (reason is null)
        {
            return;
        }

        reason.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DelayReasonApprovalRejectedHandler(IDelayReasonRepository repository, IProgressUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != DelayReasonService.SubjectType)
        {
            return;
        }

        var reason = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (reason is null)
        {
            return;
        }

        reason.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
