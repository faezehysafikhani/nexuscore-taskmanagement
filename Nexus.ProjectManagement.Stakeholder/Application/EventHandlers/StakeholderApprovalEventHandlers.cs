using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Application.EventHandlers;

public sealed class StakeholderApprovalGrantedHandler(IStakeholderRepository repository, IStakeholderUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Stakeholder")
        {
            return;
        }

        var stakeholder = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (stakeholder is null)
        {
            return;
        }

        stakeholder.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class StakeholderApprovalRejectedHandler(IStakeholderRepository repository, IStakeholderUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Stakeholder")
        {
            return;
        }

        var stakeholder = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (stakeholder is null)
        {
            return;
        }

        stakeholder.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
