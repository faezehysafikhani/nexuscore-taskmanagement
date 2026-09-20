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
