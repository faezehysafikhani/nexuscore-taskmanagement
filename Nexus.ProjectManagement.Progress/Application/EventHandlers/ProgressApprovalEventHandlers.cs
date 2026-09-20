using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Progress.Application.EventHandlers;

public sealed class ProgressApprovalGrantedHandler(IProgressRepository repository, IProgressUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "ProgressUpdate")
        {
            return;
        }

        var update = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (update is null)
        {
            return;
        }

        update.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ProgressApprovalRejectedHandler(IProgressRepository repository, IProgressUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "ProgressUpdate")
        {
            return;
        }

        var update = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (update is null)
        {
            return;
        }

        update.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
