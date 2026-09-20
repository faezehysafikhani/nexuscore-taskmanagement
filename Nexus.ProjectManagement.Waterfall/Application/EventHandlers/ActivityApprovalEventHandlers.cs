using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.EventHandlers;

public sealed class ActivityApprovalGrantedHandler(IActivityRepository repository, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "WaterfallActivity")
        {
            return;
        }

        var activity = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (activity is null)
        {
            return;
        }

        activity.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ActivityApprovalRejectedHandler(IActivityRepository repository, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "WaterfallActivity")
        {
            return;
        }

        var activity = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (activity is null)
        {
            return;
        }

        activity.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
